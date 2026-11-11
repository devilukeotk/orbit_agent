using System.Diagnostics;

namespace Orbit.WindowsAgent;

public sealed class DeveloperWorkflow(AuthorizedApplicationCatalog applications)
{
    public async Task<bool> RunAsync(
        string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(solutionPath) ||
            Path.GetExtension(solutionPath).ToLowerInvariant() is not (".sln" or ".slnx") ||
            !File.Exists(solutionPath))
            throw new ArgumentException("Select an existing .sln or .slnx file.", nameof(solutionPath));

        var workspace = Path.GetDirectoryName(solutionPath)!;
        var (executable, code) = await applications.GetVisualStudioCodeAsync(cancellationToken);

        progress.Report($"Opening {workspace} in VS Code...");
        await Task.Run(() => OpenWorkspaceAsync(executable, code.Arguments, workspace, cancellationToken), cancellationToken);
        progress.Report("Verified the VS Code workspace window.");

        if (!await RunDotnetAsync("build", solutionPath, workspace, progress, cancellationToken))
            return false;

        return await RunDotnetAsync("test", solutionPath, workspace, progress, cancellationToken);
    }

    private static async Task OpenWorkspaceAsync(
        string executable, IEnumerable<string> arguments, string workspace, CancellationToken cancellationToken)
    {
        var workspaceName = new DirectoryInfo(workspace).Name;
        bool IsWorkspaceTitle(string title) => title.Split(" - ").Contains(workspaceName, StringComparer.OrdinalIgnoreCase);
        var existing = ApplicationWindows.Find(executable, IsWorkspaceTitle).Select(window => window.Handle).ToHashSet();
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = workspace };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add("--new-window");
        startInfo.ArgumentList.Add(workspace);

        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows could not start VS Code.");
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(25))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ApplicationWindows.Find(executable, IsWorkspaceTitle).Any(window => !existing.Contains(window.Handle)))
                return;
            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("Could not verify a new VS Code window for the selected workspace.");
    }

    private static async Task<bool> RunDotnetAsync(string command, string solutionPath, string workspace,
        IProgress<string> progress, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet.exe")
        {
            WorkingDirectory = workspace,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { command, solutionPath, "--nologo", "--disable-build-servers" })
            startInfo.ArgumentList.Add(argument);
        if (command == "test")
            startInfo.ArgumentList.Add("--no-build");

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report($"Running dotnet {command}...");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows could not start dotnet.");
        var output = ReadTailAsync(process.StandardOutput, cancellationToken);
        var errors = ReadTailAsync(process.StandardError, cancellationToken);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(cancellationToken), output, errors).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // dotnet exited while cancellation was being handled.
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }

        var detail = string.Join(Environment.NewLine, (await output).Concat(await errors));
        if (detail.Length > 0)
            progress.Report(detail);
        progress.Report($"dotnet {command} exited with code {process.ExitCode}.");
        return process.ExitCode == 0;
    }

    private static async Task<IEnumerable<string>> ReadTailAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        const int retainedLines = 40;
        var lines = new Queue<string>(retainedLines);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (lines.Count == retainedLines)
                lines.Dequeue();
            lines.Enqueue(line.Length <= 2000 ? line : line[..2000] + "…");
        }
        return lines;
    }
}
