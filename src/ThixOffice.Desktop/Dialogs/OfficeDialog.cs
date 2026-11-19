using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Desktop.Controls;

namespace ThixOffice.Desktop.Dialogs;

/// <summary>
/// The frame of the apps' dialogs (ThixWrite's Seite einrichten and Zoom, ThixCalc's Zellen formatieren …), like the shell's QuestionDialog: the title as a heading, the
/// content, and the buttons in one row below, left-aligned, positive → negative (OK, Abbrechen). Enter is OK, Escape cancels.
/// <see cref="ShowAsync"/> returns true when the user confirmed.
/// </summary>
public abstract class OfficeDialog : MatWindow
{
    private bool _confirmed;

    protected OfficeDialog(string title, double width)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this.Use(BackgroundProperty, "Mat.Surface");

        OkButton = new Button { Content = new IconLabel { Icon = "check", Text = "OK" }, IsDefault = true }.Themed("MatButton.Primary");
        CancelButton = new Button { Content = new IconLabel { Icon = "x", Text = "Abbrechen" }, IsCancel = true };
        OkButton.Click += (_, _) => Confirm();
        CancelButton.Click += (_, _) => Close();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    public Button OkButton { get; }

    public Button CancelButton { get; }

    /// <summary>Puts the heading, <paramref name="body"/> and the buttons together (the derived dialog calls it once).</summary>
    protected void SetBody(Control body)
    {
        var heading = new TextBlock { Text = Title, FontSize = MatTokens.Type.FsLg, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 14) };
        var actions = new ActionBar { Margin = new Thickness(0, 22, 0, 0), Children = { OkButton, CancelButton } };
        Content = new StackPanel { Margin = new Thickness(24, 22), Children = { heading, body, actions } };
    }

    /// <summary>OK: the dialog checks and applies its values (false keeps it open).</summary>
    protected abstract bool Apply();

    /// <summary>Confirms like the OK button (tests).</summary>
    public void Confirm()
    {
        if (!Apply())
        {
            return;
        }

        _confirmed = true;
        Close();
    }

    /// <summary>Shows the dialog over <paramref name="owner"/>; true when it was confirmed.</summary>
    public async Task<bool> ShowAsync(Window owner)
    {
        if (owner.IsVisible)
        {
            await ShowDialog(owner);
        }
        else
        {
            var closed = new TaskCompletionSource();
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Closed += (_, _) => closed.TrySetResult();
            Show();
            await closed.Task;
        }

        return _confirmed;
    }

    /// <summary>A labelled field (MatFormField) around <paramref name="control"/>.</summary>
    protected static MatFormField Field(string label, Control control) => new() { Label = label, Content = control };
}
