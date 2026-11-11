using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orbit.Contracts;
using Orbit.Core;

namespace Orbit.WindowsAgent;

public sealed class AuthorizedApplicationCatalog(string path) : IApplicationCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyCollection<ApplicationCandidate>> DiscoverAsync(string query, CancellationToken cancellationToken)
    {
        var configured = await SearchAsync(query, cancellationToken);
        var discovered = StartMenuApplications.FindShortcuts(query, cancellationToken)
            .Select(shortcut => Path.GetFileNameWithoutExtension(shortcut)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !configured.Any(item => item.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .Select(name => new ApplicationCandidate(name, name, "", false));
        return configured.Concat(discovered).ToArray();
    }

    public async Task<IReadOnlyCollection<ApplicationCandidate>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var applications = await LoadAsync(cancellationToken);
        var matches = applications
            .Where(application => application.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || application.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || application.Aliases.Contains(query, StringComparer.OrdinalIgnoreCase))
            .Select(application => new ApplicationCandidate(application.Id, application.DisplayName, application.Executable, true))
            .ToArray();
        return matches;
    }

    public async Task<ApplicationCandidate?> FindAuthorizedAsync(string applicationId, CancellationToken cancellationToken)
    {
        var applications = await LoadAsync(cancellationToken);
        var application = applications.SingleOrDefault(item => item.Id.Equals(applicationId, StringComparison.Ordinal));
        return application is null
            ? null
            : new ApplicationCandidate(application.Id, application.DisplayName, application.Executable, true);
    }

    public async Task<AuthorizedApplication?> GetLaunchConfigurationAsync(string applicationId, CancellationToken cancellationToken)
    {
        var applications = await LoadAsync(cancellationToken);
        return applications.SingleOrDefault(item => item.Id.Equals(applicationId, StringComparison.Ordinal));
    }

    public async Task<(string Executable, AuthorizedApplication Application)> GetVisualStudioCodeAsync(
        CancellationToken cancellationToken)
    {
        var application = await GetLaunchConfigurationAsync("visual-studio-code", cancellationToken)
            ?? throw new ApplicationCatalogException("Add the visual-studio-code entry in Settings.");
        var executable = Environment.ExpandEnvironmentVariables(application.Executable);
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new ApplicationCatalogException("Set the VS Code entry to the full path of its installed executable.");
        return (Path.GetFullPath(executable), application);
    }

    public async Task<IReadOnlyCollection<AuthorizedApplication>> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            var configuration = await JsonSerializer.DeserializeAsync<ApplicationConfiguration>(stream, JsonOptions, cancellationToken);
            return Validate(configuration);
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ApplicationCatalogException("The authorized application registry is unavailable or invalid.");
        }
    }

    public void Authorize(AuthorizedApplication application)
    {
        LocalConfigurationFile.Update(path, current =>
        {
            var original = current ?? "{\"applications\":[]}";
            var existing = Validate(JsonSerializer.Deserialize<ApplicationConfiguration>(original, JsonOptions));
            if (existing.Any(entry => StartMenuApplications.IsSameApplication(entry, application) &&
                                      application.Aliases.All(alias => entry.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))))
                return original;

            var root = JsonNode.Parse(original, new JsonNodeOptions { PropertyNameCaseInsensitive = true })!.AsObject();
            root["applications"]!.AsArray().Add(JsonSerializer.SerializeToNode(application, JsonOptions));
            var updated = root.ToJsonString(new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
            ValidateConfiguration(updated);
            return updated;
        });
    }

    public static void ValidateConfiguration(string json) =>
        Validate(JsonSerializer.Deserialize<ApplicationConfiguration>(json, JsonOptions));

    private static IReadOnlyCollection<AuthorizedApplication> Validate(ApplicationConfiguration? configuration)
    {
        if (configuration?.Applications is null)
            throw new ApplicationCatalogException("The application registry has no applications list.");

        var applications = configuration.Applications.OfType<AuthorizedApplication>().ToArray();
        if (applications.Any(application => !IsValid(application)) ||
            applications.Select(application => application.Id).Distinct(StringComparer.Ordinal).Count() != applications.Length)
            throw new ApplicationCatalogException("The application registry has invalid or duplicate application entries.");
        return applications;
    }

    private static bool IsValid(AuthorizedApplication application) =>
        !string.IsNullOrWhiteSpace(application.Id) && application.Id.Length <= 100 &&
        !string.IsNullOrWhiteSpace(application.DisplayName) && application.DisplayName.Length <= 200 &&
        !string.IsNullOrWhiteSpace(application.Executable) && application.Executable.Length <= 32_767 &&
        (application.WorkingDirectory is null ||
         (Path.IsPathFullyQualified(application.WorkingDirectory) && application.WorkingDirectory.Length <= 32_767)) &&
        application.Arguments is not null && application.Arguments.All(argument => argument is not null && argument.Length <= 4_000) &&
        application.Aliases is not null && application.Aliases.All(alias => !string.IsNullOrWhiteSpace(alias) && alias.Length <= 200);

    private sealed record ApplicationConfiguration(IReadOnlyCollection<AuthorizedApplication?>? Applications);
}

public sealed class AuthorizedApplication
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Executable { get; init; } = string.Empty;
    public string? WorkingDirectory { get; init; }
    public IReadOnlyCollection<string> Arguments { get; init; } = [];
    public IReadOnlyCollection<string> Aliases { get; init; } = [];
}

public sealed class WindowsApplicationLauncher(AuthorizedApplicationCatalog catalog) : IApplicationLauncher
{
    public async Task<ToolExecutionResult> LaunchAsync(ApplicationCandidate application, CancellationToken cancellationToken)
    {
        var configuredApplication = await catalog.GetLaunchConfigurationAsync(application.Id, cancellationToken)
            ?? throw new ApplicationCatalogException("The application is no longer registered.");
        return LaunchApproved(configuredApplication, cancellationToken);
    }

    public static ToolExecutionResult LaunchApproved(AuthorizedApplication configuredApplication, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ExpandEnvironmentVariables(configuredApplication.Executable),
                WorkingDirectory = configuredApplication.WorkingDirectory ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in configuredApplication.Arguments)
                startInfo.ArgumentList.Add(argument);

            cancellationToken.ThrowIfCancellationRequested();
            using var process = Process.Start(startInfo);
            if (process is null)
                return new ToolExecutionResult(false, $"Windows did not start {configuredApplication.DisplayName}.");

            return new ToolExecutionResult(true, $"Opened {configuredApplication.DisplayName}.",
                $"Windows created process {process.Id}.");
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ToolExecutionResult(false, $"Windows could not open {configuredApplication.DisplayName}: {error.Message}");
        }
    }
}

public sealed class ApplicationCatalogException(string message) : Exception(message);
