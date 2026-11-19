using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Models;

namespace ThixOffice.Desktop.Shell;

/// <summary>What the cloud place at the right of the title bar shows.</summary>
public enum CloudIndicatorKind
{
    /// <summary>Nothing (no cloud, not signed in and a local document).</summary>
    Hidden,

    /// <summary>Signed in, the document is local: a quiet cloud.</summary>
    SignedIn,

    /// <summary>A ThixDrive document whose version is in the Drive.</summary>
    InDrive,

    Uploading,

    /// <summary>Saved here, not in the Drive: a click tries again.</summary>
    NotUploaded,

    /// <summary>Somebody saved in between: a click asks what to do.</summary>
    Conflict,

    Offline,

    /// <summary>The cloud ended the sign-in, or switched the Drive off.</summary>
    Problem,
}

/// <summary>
/// The state of ThixCloud and of the window's document at the right of the title bar (Mat tokens only): "In ThixDrive", "Wird
/// hochgeladen …", "Nicht hochgeladen" (warning; a click uploads again), "Konflikt" (a click asks), "Offline", "Abgemeldet".
/// What does not apply is hidden: an app without the cloud, or not signed in with a local document, shows nothing.
/// </summary>
public sealed class CloudIndicator : Button
{
    private readonly MatIcon _icon = new() { Size = 16, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), FontSize = 12 };

    public CloudIndicator()
    {
        Theme = Ui.Theme("MatButton.Ghost");
        Padding = new Thickness(8, 3);
        Margin = new Thickness(8, 0, 4, 0);
        VerticalAlignment = VerticalAlignment.Center;
        Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _icon, _text } };
        IsVisible = false;
    }

    public CloudIndicatorKind Kind { get; private set; }

    /// <summary>The words shown (tests).</summary>
    public string Text => _text.Text ?? "";

    /// <summary>Shows the state of <paramref name="document"/> and of the connection.</summary>
    public void Refresh(OfficeDocument document, CloudStatus status, bool hasCloud)
    {
        var (kind, icon, text, tip, brush) = Describe(document, status, hasCloud);
        Kind = kind;
        IsVisible = kind != CloudIndicatorKind.Hidden;
        _icon.Icon = icon;
        _icon.Use(MatIcon.ForegroundProperty, brush);
        _text.Text = text;
        _text.IsVisible = text.Length > 0;
        _text.Use(TextBlock.ForegroundProperty, brush);
        ToolTip.SetTip(this, tip);
        AutomationProperties.SetName(this, text.Length > 0 ? text : tip);
    }

    private static (CloudIndicatorKind Kind, string Icon, string Text, string Tip, string Brush) Describe(OfficeDocument document, CloudStatus status, bool hasCloud)
    {
        if (!hasCloud)
        {
            return (CloudIndicatorKind.Hidden, "cloud", "", "", "Mat.TextMuted");
        }

        if (document.Drive is { } drive)
        {
            switch (document.DriveState)
            {
                case DriveSyncState.Uploading:
                    return (CloudIndicatorKind.Uploading, "upload", "Wird hochgeladen …", $"„{drive.Name}“ wird in ThixDrive gespeichert.", "Mat.TextMuted");
                case DriveSyncState.NotUploaded:
                    return (CloudIndicatorKind.NotUploaded, "alert", "Nicht hochgeladen",
                        "Auf diesem Computer gespeichert, aber noch nicht in ThixDrive. Klicken Sie, um es erneut zu versuchen.", "Mat.Warning");
                case DriveSyncState.Conflict:
                    return (CloudIndicatorKind.Conflict, "alert", "Konflikt",
                        "Die Datei wurde in ThixDrive inzwischen geändert. Klicken Sie, um zu entscheiden, wie gespeichert wird.", "Mat.Danger");
            }

            if (status.State == CloudState.Offline)
            {
                return (CloudIndicatorKind.Offline, "alert", "Offline", $"ThixDrive: {drive.DisplayPath} – die ThixCloud ist gerade nicht erreichbar.", "Mat.TextMuted");
            }

            if (status.State is CloudState.SignedOut or CloudState.Forbidden or CloudState.Off)
            {
                return (CloudIndicatorKind.Problem, "alert", status.State == CloudState.Forbidden ? "ThixDrive abgeschaltet" : "Abgemeldet", status.Message, "Mat.Warning");
            }

            return (CloudIndicatorKind.InDrive, "cloud", "In ThixDrive", $"Gespeichert in ThixDrive: {drive.DisplayPath}", "Mat.Accent");
        }

        return status.State switch
        {
            CloudState.Online or CloudState.Connecting => (CloudIndicatorKind.SignedIn, "cloud", "", $"ThixCloud: {status.Message}", "Mat.TextMuted"),
            CloudState.Offline => (CloudIndicatorKind.Offline, "alert", "Offline", status.Message, "Mat.TextMuted"),
            CloudState.SignedOut or CloudState.Forbidden => (CloudIndicatorKind.Problem, "alert", status.State == CloudState.Forbidden ? "ThixDrive abgeschaltet" : "Abgemeldet", status.Message, "Mat.Warning"),
            _ => (CloudIndicatorKind.Hidden, "cloud", "", "", "Mat.TextMuted"),
        };
    }
}
