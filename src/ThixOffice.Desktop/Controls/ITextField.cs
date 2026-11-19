namespace ThixOffice.Desktop.Controls;

/// <summary>
/// A text field of an app that is not a <see cref="Avalonia.Controls.TextBox"/> (ThixCalc's cell and formula editor): while it
/// has the keyboard, commands with <c>YieldsToTextField</c> (undo, copy, paste, select all) leave their keys to it, as they
/// do for a TextBox. <see cref="IsEditingText"/> false lets the window's shortcuts apply again (a grid that only looks like
/// a field while no cell is edited).
/// </summary>
public interface ITextField
{
    bool IsEditingText { get; }
}
