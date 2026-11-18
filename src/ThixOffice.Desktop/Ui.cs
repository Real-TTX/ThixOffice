using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using MatDesktop;
using ThixOffice.Core.Infrastructure;

namespace ThixOffice.Desktop;

/// <summary>
/// Small helpers for code-built UI: theme resources by key, the word mark, shortcuts as the tooltips name them, the UI
/// thread, opening folders and addresses. Colours, sizes and named looks are MatDesktop's (<c>control.Use(property,
/// "Mat.TextMuted")</c>, <see cref="Theme"/>); icons are <c>MatIcon</c> with the name of the sprite.
/// </summary>
public static class Ui
{
    /// <summary>A resource of the theme (brush, control theme, size) by key.</summary>
    public static object Resource(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) && value is not null
            ? value
            : throw new InvalidOperationException($"Resource {key} is missing (MatDesktop/Themes).");

    public static IBrush Brush(string key) => (IBrush)Resource(key);

    /// <summary>A named look of MatDesktop (a control theme such as "MatButton.Primary").</summary>
    public static ControlTheme Theme(string key) => (ControlTheme)Resource(key);

    /// <summary>Runs the action on the UI thread (events of the business layer come from any thread).</summary>
    public static void Post(Action action) => Dispatcher.UIThread.Post(action);

    /// <summary>The window that hosts a control (null while it is not shown).</summary>
    public static Window? WindowOf(Control control) => TopLevel.GetTopLevel(control) as Window;

    /// <summary>
    /// The word mark: "Thix" in the text colour and the app's suffix ("Write") in the accent text. Bound to the theme, so it
    /// follows light and dark.
    /// </summary>
    public static TextBlock WordMark(string suffix, double? fontSize = null)
    {
        var mark = new TextBlock { FontSize = fontSize ?? MatTokens.Type.FsXl, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var accent = new Run(suffix);
        accent.Bind(TextElement.ForegroundProperty, mark.GetResourceObservable("Mat.AccentText"));
        mark.Inlines = [new Run(Core.Models.OfficeAppDefinition.SuiteMarkPrefix), accent];
        return mark;
    }

    /// <summary>
    /// A shortcut of a command ("Ctrl+Shift+S", "F12") as the tooltips name it: "Strg+Umschalt+S" on Windows, "⌘⇧S" on macOS.
    /// </summary>
    public static string ShortcutText(string shortcut)
    {
        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (OperatingSystem.IsMacOS())
        {
            // The Mac order of the modifiers: ⌥ ⇧ ⌘, then the key ("Ctrl+Alt+R" is ⌥⌘R).
            var order = new[] { "Alt", "Shift", "Ctrl" };
            var modifiers = order.Where(parts.Contains).Select(part => part switch
            {
                "Ctrl" => "⌘",
                "Shift" => "⇧",
                _ => "⌥",
            });
            return string.Concat(modifiers.Concat(parts.Where(part => !order.Contains(part))));
        }

        return string.Join('+', parts.Select(part => part switch
        {
            "Ctrl" => "Strg",
            "Shift" => "Umschalt",
            _ => part,
        }));
    }

    /// <summary>The key gesture of a shortcut; "Ctrl" is the command key (⌘ on macOS). Null when it cannot be read.</summary>
    public static KeyGesture? Gesture(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return null;
        }

        var text = OperatingSystem.IsMacOS() ? shortcut.Replace("Ctrl", "Cmd", StringComparison.Ordinal) : shortcut;
        try
        {
            return KeyGesture.Parse(text);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>"Speichern (Strg+S)" – the tooltip of a command.</summary>
    public static string Tooltip(string label, string? shortcut) => shortcut is null ? label : $"{label} ({ShortcutText(shortcut)})";

    /// <summary>Tests see what would be opened instead of a browser opening on the machine that runs them.</summary>
    public static Action<string>? OpenExternalForTests { get; set; }

    /// <summary>Opens a web address or a folder with the program the system uses for it (browser, Explorer, Finder).</summary>
    public static void OpenExternal(string pathOrUrl)
    {
        if (OpenExternalForTests is { } open)
        {
            open(pathOrUrl);
            return;
        }

        try
        {
            var startInfo = OperatingSystem.IsMacOS()
                ? new System.Diagnostics.ProcessStartInfo("open") { ArgumentList = { pathOrUrl }, UseShellExecute = false }
                : OperatingSystem.IsLinux()
                    ? new System.Diagnostics.ProcessStartInfo("xdg-open") { ArgumentList = { pathOrUrl }, UseShellExecute = false }
                    : new System.Diagnostics.ProcessStartInfo(pathOrUrl) { UseShellExecute = true };
            System.Diagnostics.Process.Start(startInfo)?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warning($"Could not open an address or folder: {exception.Message}");
        }
    }

    /// <summary>Shows a file in Explorer (selected) or Finder.</summary>
    public static void RevealFile(string path)
    {
        if (OpenExternalForTests is { } open)
        {
            open(path);
            return;
        }

        try
        {
            var startInfo = OperatingSystem.IsWindows()
                ? new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                : OperatingSystem.IsMacOS()
                    ? new System.Diagnostics.ProcessStartInfo("open") { ArgumentList = { "-R", path } }
                    : new System.Diagnostics.ProcessStartInfo("xdg-open") { ArgumentList = { Path.GetDirectoryName(path)! } };
            startInfo.UseShellExecute = false;
            System.Diagnostics.Process.Start(startInfo)?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warning($"Could not show a file: {exception.Message}");
        }
    }

    /// <summary>"Heute, 13:47" / "Gestern, 13:47" / "12.11.2026, 13:47".</summary>
    public static string FormatDateTime(DateTimeOffset value, DateTime? today = null)
    {
        var local = value.ToLocalTime();
        var day = today ?? DateTime.Today;
        if (local.Date == day)
        {
            return $"Heute, {local:HH:mm}";
        }

        return local.Date == day.AddDays(-1) ? $"Gestern, {local:HH:mm}" : local.ToString("dd.MM.yyyy, HH:mm", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
    }
}
