using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Orbit.WindowsAgent;

public sealed record ApplicationWindow(nint Handle, uint ProcessId, string Title, string? Executable);

public enum WindowFocusResult { Focused, Closed, Rejected }

public static class ApplicationWindows
{
    public static IReadOnlyList<ApplicationWindow> ListOpen() =>
        Enumerate().Where(window => window.ProcessId != Environment.ProcessId).ToArray();

    public static IReadOnlyList<ApplicationWindow> Find(string executable, Func<string, bool>? matchTitle = null) =>
        Enumerate(matchTitle)
            .Where(window => string.Equals(window.Executable, executable, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public static IReadOnlyList<ApplicationWindow> Search(string query, IReadOnlyCollection<AuthorizedApplication> aliases)
    {
        var names = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var registered = aliases
            .Where(app => new[] { app.Id, app.DisplayName }.Concat(app.Aliases).Any(name => SameName(name, query)))
            .Select(app => Environment.ExpandEnvironmentVariables(app.Executable))
            .ToArray();
        var windows = ListOpen();
        var exact = windows.Where(window =>
        {
            if (window.Executable is not { } executable)
                return false;
            if (!names.TryGetValue(executable, out var appNames))
                names[executable] = appNames = GetApplicationNames(executable);
            return registered.Any(path => string.Equals(
                       Path.IsPathFullyQualified(path) ? executable : Path.GetFileName(executable),
                       path, StringComparison.OrdinalIgnoreCase)) ||
                   appNames.Any(name => SameName(name, query));
        }).ToArray();
        if (exact.Length > 0)
            return exact;

        var exactTitles = windows.Where(window => SameName(window.Title, query)).ToArray();
        return exactTitles.Length > 0
            ? exactTitles
            : windows.Where(window => window.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private static bool SameName(string name, string query) =>
        string.Equals(string.Concat(name.Where(character => !char.IsWhiteSpace(character))),
            string.Concat(query.Where(character => !char.IsWhiteSpace(character))), StringComparison.OrdinalIgnoreCase);

    private static string[] GetApplicationNames(string executable)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(executable);
            return new[] { Path.GetFileNameWithoutExtension(executable), version.ProductName, version.FileDescription }
                .OfType<string>().Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return [Path.GetFileNameWithoutExtension(executable)];
        }
    }

    private static IReadOnlyList<ApplicationWindow> Enumerate(Func<string, bool>? matchTitle = null)
    {
        var windows = new List<ApplicationWindow>();
        var executables = new Dictionary<uint, string?>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
                return true;

            var title = new StringBuilder(1024);
            GetWindowText(handle, title, title.Capacity);
            var text = title.ToString();
            if (string.IsNullOrWhiteSpace(text) || (matchTitle is not null && !matchTitle(text)))
                return true;

            if (GetWindowThreadProcessId(handle, out var processId) == 0)
                return true;
            if (!executables.TryGetValue(processId, out var executable))
                executables[processId] = executable = GetExecutable(processId);
            windows.Add(new ApplicationWindow(handle, processId, text, executable));
            return true;
        }, 0);
        return windows;
    }

    public static async Task<WindowFocusResult> FocusAsync(ApplicationWindow window)
    {
        if (!IsSameWindow(window))
            return WindowFocusResult.Closed;

        const int restore = 9;
        if (IsIconic(window.Handle))
            ShowWindowAsync(window.Handle, restore);

        SetForegroundWindow(window.Handle);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (GetForegroundWindow() == window.Handle && !IsIconic(window.Handle))
                return IsSameWindow(window) ? WindowFocusResult.Focused : WindowFocusResult.Closed;
            await Task.Delay(50);
        }

        return IsSameWindow(window) ? WindowFocusResult.Rejected : WindowFocusResult.Closed;
    }

    public static bool IsSameWindow(ApplicationWindow window) =>
        IsWindowVisible(window.Handle) &&
        GetWindowThreadProcessId(window.Handle, out var processId) != 0 &&
        processId == window.ProcessId &&
        (window.Executable is null ||
         string.Equals(GetExecutable(processId), window.Executable, StringComparison.OrdinalIgnoreCase));

    private static string? GetExecutable(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.MainModule?.FileName;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private delegate bool WindowCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
