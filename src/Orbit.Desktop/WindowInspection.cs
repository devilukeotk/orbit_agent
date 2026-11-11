using System.Text;
using System.Windows.Automation;
using Orbit.WindowsAgent;

namespace Orbit.Desktop;

internal sealed record WindowInspection(string Outline, int ControlCount, bool Truncated);

internal static class WindowInspector
{
    private const int MaxControls = 200;
    private const int MaxDepth = 12;

    private static readonly (AutomationProperty Property, string Name)[] Capabilities =
    [
        (AutomationElement.IsInvokePatternAvailableProperty, "Invoke"),
        (AutomationElement.IsValuePatternAvailableProperty, "Value"),
        (AutomationElement.IsTextPatternAvailableProperty, "Text"),
        (AutomationElement.IsScrollPatternAvailableProperty, "Scroll"),
        (AutomationElement.IsTogglePatternAvailableProperty, "Toggle"),
        (AutomationElement.IsSelectionPatternAvailableProperty, "Selection"),
        (AutomationElement.IsSelectionItemPatternAvailableProperty, "Selection item"),
        (AutomationElement.IsExpandCollapsePatternAvailableProperty, "Expand/collapse")
    ];

    public static WindowInspection Read(ApplicationWindow window, CancellationToken cancellationToken)
    {
        if (!ApplicationWindows.IsSameWindow(window))
            throw new ElementNotAvailableException();

        var root = AutomationElement.FromHandle(window.Handle);
        if (root is null || root.Current.ProcessId != window.ProcessId)
            throw new ElementNotAvailableException();

        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.IsEnabledProperty);
        cache.Add(AutomationElement.IsPasswordProperty);
        foreach (var capability in Capabilities)
            cache.Add(capability.Property);

        var walker = TreeWalker.ControlViewWalker;
        var outline = new StringBuilder();
        var count = 0;
        var truncated = false;

        void ReadChildren(AutomationElement parent, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var child = walker.GetFirstChild(parent, cache);
            while (child is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (count == MaxControls || depth == MaxDepth)
                {
                    truncated = true;
                    return;
                }

                var info = child.Cached;
                var name = info.IsPassword ? "" : CleanName(info.Name);
                outline.Append(' ', depth * 2).Append(info.ControlType?.LocalizedControlType ?? "control");
                if (name.Length > 0)
                    outline.Append("  ").Append(name);
                if (!info.IsEnabled)
                    outline.Append("  (disabled)");
                var patterns = Capabilities.Where(capability =>
                    child.GetCachedPropertyValue(capability.Property, ignoreDefaultValue: true) is true)
                    .Select(capability => capability.Name).ToArray();
                if (patterns.Length > 0)
                    outline.Append("  [").AppendJoin(", ", patterns).Append(']');
                outline.AppendLine();

                count++;
                ReadChildren(child, depth + 1);
                if (count == MaxControls)
                {
                    truncated = true;
                    return;
                }
                child = walker.GetNextSibling(child, cache);
            }
        }

        ReadChildren(root, 0);
        if (!ApplicationWindows.IsSameWindow(window))
            throw new ElementNotAvailableException();

        return new WindowInspection(outline.ToString(), count, truncated);
    }

    private static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        var cleaned = name.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        return cleaned.Length > 160 ? cleaned[..160] + "…" : cleaned;
    }
}
