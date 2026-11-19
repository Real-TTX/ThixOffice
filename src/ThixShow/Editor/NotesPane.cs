using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Desktop;

namespace ThixShow.Editor;

/// <summary>
/// The notes of the current slide below it (PowerPoint's "Klicken Sie, um Notizen hinzuzufügen"): plain paragraphs, its font size
/// follows Strg+mouse wheel. What is typed goes into the presentation as one undo step when the pane loses the keyboard, the slide
/// changes or the document is saved.
/// </summary>
internal sealed class NotesPane : Border
{
    private readonly ShowEditor _editor;
    private uint? _slideId;
    private string _loaded = "";

    public NotesPane(ShowEditor editor)
    {
        _editor = editor;
        Box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Watermark = "Klicken Sie, um Notizen hinzuzufügen",
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 10),
            FontSize = 14,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        Box.Use(TextBox.BackgroundProperty, "Mat.Surface");
        Avalonia.Automation.AutomationProperties.SetName(Box, "Notizen");
        Box.LostFocus += (_, _) => Commit();
        Box.GotFocus += (_, _) => _editor.FocusedArea = EditorArea.Notes;
        Box.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        Child = Box;
        this.Use(Border.BackgroundProperty, "Mat.Surface");
    }

    public TextBox Box { get; }

    /// <summary>True while the text differs from the notes it was loaded with (typed, not yet in the presentation).</summary>
    private bool IsDirty => (Box.Text ?? "") != _loaded;

    /// <summary>Shows the notes of the current slide (committing what was typed for the one before).</summary>
    public void Load()
    {
        var slide = _editor.Session.CurrentSlide;
        if (slide?.Id == _slideId && !IsDirty)
        {
            var text = slide?.NotesText ?? "";
            if (Box.Text != text && !Box.IsFocused)
            {
                Set(text);
            }

            return;
        }

        Commit();
        _slideId = slide?.Id;
        Set(slide?.NotesText ?? "");
    }

    /// <summary>Another presentation came into the window: what was typed belonged to the old one and is dropped.</summary>
    public void Reset()
    {
        _slideId = null;
        Set("");
    }

    private void Set(string text)
    {
        _loaded = text;
        Box.Text = text;
    }

    /// <summary>Puts what was typed into the presentation (one undo step).</summary>
    public void Commit()
    {
        if (!IsDirty || _slideId is not { } id)
        {
            return;
        }

        var text = Box.Text ?? "";
        _loaded = text;
        if (_editor.Session.Presentation.FindSlide(id) is { } slide && slide.NotesText != text)
        {
            _ = _editor.RunAsync("Notizen", () => _editor.Session.SetNotesText(id, text));
        }
    }

    /// <summary>Strg + mouse wheel: the notes' font size (PowerPoint zooms its notes pane).</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (!e.KeyModifiers.HasFlag(modifier) || e.Delta.Y == 0)
        {
            return;
        }

        e.Handled = true;
        Box.FontSize = Math.Clamp(Box.FontSize + (e.Delta.Y > 0 ? 2 : -2), 8, 48);
    }
}
