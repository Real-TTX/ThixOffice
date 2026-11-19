using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using MatDesktop;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop.Controls;

/// <summary>
/// A ribbon button of an app's own that opens a flyout whose content the app builds each time it opens (galleries with pictures the
/// ribbon model cannot describe: layouts, designs, shapes). Large (icon above the label, a chevron below) or small (icon and
/// label in a row); enabled while the app's command of the same id is. Placed through <c>OfficeApplication.CreateRibbonContent</c>.
/// </summary>
public sealed class RibbonFlyoutButton : Button
{
    private readonly OfficeWindow _window;
    private readonly string _commandId;
    private readonly Func<RibbonFlyoutButton, Control> _content;
    private readonly Flyout _flyout;

    public RibbonFlyoutButton(OfficeWindow window, string commandId, string label, string icon, Func<RibbonFlyoutButton, Control> content, bool large = true, string? tooltip = null)
    {
        _window = window;
        _commandId = commandId;
        _content = content;
        this.Themed("MatRibbon.Item");
        if (large)
        {
            Height = MatRibbon.ContentHeight;
            MinWidth = 46;
            Padding = new Thickness(4, 0, 4, 4);
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Top;
            Content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new MatIcon { Icon = icon, Size = 26, Margin = new Thickness(0, 6, 0, 2), HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center },
                    new MatIcon { Icon = "chevron-down", Size = 12, HorizontalAlignment = HorizontalAlignment.Center },
                },
            };
        }
        else
        {
            Padding = new Thickness(6, 2);
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new MatIcon { Icon = icon, Size = 16 },
                    new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                    new MatIcon { Icon = "chevron-down", Size = 10, VerticalAlignment = VerticalAlignment.Center },
                },
            };
        }

        AutomationProperties.SetName(this, label);
        ToolTip.SetTip(this, tooltip ?? label);
        _flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Standard };
        Click += (_, _) => Open();
        window.Commands.StateChanged += (_, _) => Ui.Post(SyncEnabled);
        SyncEnabled();
    }

    /// <summary>True while the flyout is open.</summary>
    public bool IsOpen => _flyout.IsOpen;

    /// <summary>The content of the open flyout (tests reach its entries).</summary>
    public Control? FlyoutContent => _flyout.Content as Control;

    /// <summary>Builds the content anew and opens the flyout.</summary>
    public void Open()
    {
        _flyout.Content = _content(this);
        _flyout.ShowAt(this);
    }

    /// <summary>Closes the flyout and gives the keyboard back to the document (after a choice).</summary>
    public void Close()
    {
        _flyout.Hide();
        _window.Editor.FocusDocument();
    }

    private void SyncEnabled() => IsEnabled = _window.Commands.Find(_commandId)?.IsEnabled ?? false;
}
