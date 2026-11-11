using System.Runtime.InteropServices;

namespace Orbit.WindowsAgent;

public static class StartMenuApplications
{
    internal static IEnumerable<string> FindShortcuts(string query, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms }
            .Select(folder => Environment.GetFolderPath(folder))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.lnk", options))
            .Where(shortcut =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Path.GetFileNameWithoutExtension(shortcut).Contains(query, StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(shortcut => Path.GetFileNameWithoutExtension(shortcut).Equals(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<AuthorizedApplication> Find(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200)
            return [];

        var candidates = new List<AuthorizedApplication>();
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
        try
        {
            foreach (var path in FindShortcuts(query, CancellationToken.None))
            {
                var app = ReadShortcut(shell, path, query);
                if (app is null || candidates.Any(existing => IsSameApplication(existing, app)))
                    continue;
                candidates.Add(app);
                if (candidates.Count == 50)
                    break;
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }

        return candidates;
    }

    internal static bool IsSameApplication(AuthorizedApplication first, AuthorizedApplication second) =>
        first.DisplayName.Equals(second.DisplayName, StringComparison.OrdinalIgnoreCase) &&
        first.Executable.Equals(second.Executable, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(first.WorkingDirectory, second.WorkingDirectory, StringComparison.OrdinalIgnoreCase) &&
        first.Arguments.SequenceEqual(second.Arguments, StringComparer.Ordinal);

    private static AuthorizedApplication? ReadShortcut(dynamic shell, string path, string query)
    {
        object? shortcutObject = null;
        try
        {
            shortcutObject = shell.CreateShortcut(path);
            dynamic shortcut = shortcutObject!;
            var executable = Environment.ExpandEnvironmentVariables((string)shortcut.TargetPath);
            if (!Path.IsPathFullyQualified(executable) ||
                !Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
                return null;

            var workingDirectory = Environment.ExpandEnvironmentVariables((string)shortcut.WorkingDirectory);
            var arguments = ParseArguments((string)shortcut.Arguments);
            return new AuthorizedApplication
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = Path.GetFileNameWithoutExtension(path),
                Executable = Path.GetFullPath(executable),
                WorkingDirectory = Path.IsPathFullyQualified(workingDirectory) && Directory.Exists(workingDirectory)
                    ? Path.GetFullPath(workingDirectory)
                    : Path.GetDirectoryName(executable),
                Arguments = arguments,
                Aliases = [query]
            };
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (shortcutObject is not null)
                Marshal.FinalReleaseComObject(shortcutObject);
        }
    }

    private static string[] ParseArguments(string arguments)
    {
        var argv = CommandLineToArgvW("app.exe " + arguments, out var count);
        if (argv == 0)
            throw new System.ComponentModel.Win32Exception();
        try
        {
            var result = new string[count - 1];
            for (var i = 1; i < count; i++)
                result[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * nint.Size))!;
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
