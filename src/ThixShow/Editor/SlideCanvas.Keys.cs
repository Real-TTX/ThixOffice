using Avalonia.Input;
using ThixShow.Core.Editing;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;

namespace ThixShow.Editor;

/// <summary>
/// The keys of the slide (after the window's command shortcuts): with text being edited – typing, caret movement (Strg/⌥ words,
/// Pos1/Ende, ↑/↓ by lines), Rücktaste/Entf, Eingabe (new paragraph), Umschalt+Eingabe (line break), Tab/Umschalt+Tab (list level),
/// Esc (back to the shape); with shapes selected – arrows nudge (Strg/⌘ by a point), Entf deletes, Tab cycles the shapes,
/// Eingabe/F2 edits the text, typing starts the text, Esc leaves a group or clears the selection.
/// </summary>
internal sealed partial class SlideCanvas
{
    /// <summary>A nudge with an arrow key (PowerPoint: one grid step of 0.2 cm); with Strg/⌘ one point.</summary>
    private const long NudgeStep = 72000;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        e.Handled = HandleKey(e.Key, e.KeyModifiers);
    }

    internal bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (Slide is null)
        {
            return false;
        }

        return Session.Selection.Text is not null ? HandleTextKey(key, modifiers) : HandleShapeKey(key, modifiers);
    }

    private bool HandleTextKey(Key key, KeyModifiers modifiers)
    {
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        var word = modifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Alt : KeyModifiers.Control);
        var line = OperatingSystem.IsMacOS() && modifiers.HasFlag(KeyModifiers.Meta);
        switch (key)
        {
            case Key.Left:
                Session.MoveCaret(line ? CaretMovement.ParagraphStart : word ? CaretMovement.WordLeft : CaretMovement.Left, shift);
                break;
            case Key.Right:
                Session.MoveCaret(line ? CaretMovement.ParagraphEnd : word ? CaretMovement.WordRight : CaretMovement.Right, shift);
                break;
            case Key.Up or Key.Down when line || word:
                Session.MoveCaret(key == Key.Up ? CaretMovement.TextStart : CaretMovement.TextEnd, shift);
                break;
            case Key.Up or Key.Down:
                MoveCaretByLine(key == Key.Up ? -1 : 1, shift);
                break;
            case Key.Home:
                Session.MoveCaret(word ? CaretMovement.TextStart : CaretMovement.ParagraphStart, shift);
                break;
            case Key.End:
                Session.MoveCaret(word ? CaretMovement.TextEnd : CaretMovement.ParagraphEnd, shift);
                break;
            case Key.Back:
                Run("Löschen", () => Session.DeleteBackward(word));
                break;
            case Key.Delete:
                Run("Löschen", () => Session.DeleteForward(word));
                break;
            case Key.Enter when shift:
                Run("Zeilenumbruch", Session.InsertLineBreak);
                break;
            case Key.Enter:
                Run("Absatz", Session.InsertParagraphBreak);
                break;
            case Key.Tab:
                Run("Ebene", () => Session.HandleTab(shift));
                break;
            case Key.Escape:
                Session.EndTextEditing();
                break;
            default:
                return false;
        }

        Overlay.ResetBlink();
        return true;
    }

    /// <summary>↑/↓ in text: the position on the line above/below at the caret's x (PowerPoint's way).</summary>
    private void MoveCaretByLine(int direction, bool extend)
    {
        if (Session.Selection.Text is not { } text || EditedText is not { } placed)
        {
            return;
        }

        var layout = placed.Layout;
        var (x, top, height) = layout.CaretAt(text.Active.Paragraph, text.Active.Offset);
        var y = direction < 0 ? top - (height / 2) : top + height + (height / 2);
        if (y < layout.Lines.FirstOrDefault()?.Top - 0.01 || y > layout.Lines.LastOrDefault()?.Bottom + 0.01)
        {
            Session.MoveCaret(direction < 0 ? CaretMovement.TextStart : CaretMovement.TextEnd, extend);
            return;
        }

        var (paragraph, offset) = layout.HitTest(x, y);
        Session.SetCaret(text.Target, new TextPosition(paragraph, offset), extend);
    }

    private bool HandleShapeKey(Key key, KeyModifiers modifiers)
    {
        var ids = SelectedIds;
        var fine = modifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        var step = fine ? Units.EmuPerPoint : NudgeStep;
        switch (key)
        {
            case Key.Left or Key.Right or Key.Up or Key.Down when ids.Count > 0:
                var (dx, dy) = key switch
                {
                    Key.Left => (-step, 0L),
                    Key.Right => (step, 0L),
                    Key.Up => (0L, -step),
                    _ => (0L, step),
                };
                Run("Verschieben", () => Session.MoveShapes(dx, dy, nudge: true));
                return true;
            case Key.Delete or Key.Back when ids.Count > 0:
                Run("Löschen", () => Session.DeleteShapes());
                return true;
            case Key.Tab:
                SelectNext(modifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                return true;
            case Key.Enter or Key.F2 when ids.Count == 1:
                return EditTextOf(ids[0], selectAll: key == Key.F2);
            case Key.Escape when Session.Selection.EnteredGroup is { } group:
                Session.SelectShapes([group]);
                return true;
            case Key.Escape when ids.Count > 0:
                Session.SelectShapes([]);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Tab / Umschalt+Tab: the next / previous shape in the z-order (inside an entered group: its children).</summary>
    private void SelectNext(int direction)
    {
        if (Slide is not { } slide)
        {
            return;
        }

        var entered = Session.Selection.EnteredGroup;
        var shapes = (entered is { } group && slide.FindShape(group) is GroupShape opened ? opened.Children : slide.Shapes).Where(shape => !shape.Hidden).ToList();
        if (shapes.Count == 0)
        {
            return;
        }

        var current = SelectedIds.Count > 0 ? shapes.FindIndex(shape => shape.Id == SelectedIds[^1]) : -1;
        var next = current < 0 ? (direction > 0 ? 0 : shapes.Count - 1) : (current + direction + shapes.Count) % shapes.Count;
        Session.SelectShapes([shapes[next].Id], entered);
    }

    /// <summary>Starts editing a shape's text: the caret at its end, or all of it selected (F2, like PowerPoint).</summary>
    internal bool EditTextOf(int shapeId, bool selectAll = false)
    {
        if (Session.TextBodyOf(new TextTarget(shapeId)) is not { } body || Slide?.FindShape(shapeId) is not AutoShape)
        {
            return false;
        }

        var last = body.Paragraphs.Count - 1;
        var end = new TextPosition(last, body.Paragraphs[last].Length);
        Session.SelectText(new TextTarget(shapeId), selectAll ? new TextPosition(0, 0) : end, end);
        Overlay.ResetBlink();
        return true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl))
        {
            return;
        }

        if (Session.Selection.Text is null)
        {
            // Typing on a selected shape writes into it (PowerPoint replaces nothing: the text continues at its end).
            if (SelectedIds.Count != 1 || !EditTextOf(SelectedIds[0]))
            {
                return;
            }
        }

        e.Handled = true;
        SetPreedit(null);
        var text = e.Text;
        Run("Eingabe", () => Session.InsertText(text));
        Overlay.ResetBlink();
    }

    protected override void OnGotFocus(Avalonia.Input.GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _editor.FocusedArea = EditorArea.Slide;
        Overlay.ResetBlink();
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        Overlay.ResetBlink();
    }

    private void Run(string label, Action action) => _ = _editor.RunAsync(label, action);
}
