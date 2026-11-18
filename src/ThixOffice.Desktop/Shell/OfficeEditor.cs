using Avalonia.Controls;

namespace ThixOffice.Desktop.Shell;

/// <summary>
/// The app's part of a document window: the control in the editor area for one document. ThixWrite's text editor,
/// ThixCalc's grid and ThixShow's slide canvas derive from it; the shell asks it for the focus and the status bar texts.
/// </summary>
public abstract class OfficeEditor
{
    /// <summary>The control in the editor area.</summary>
    public abstract Control View { get; }

    /// <summary>Puts the keyboard into the document (after the backstage, the search, opening).</summary>
    public virtual void FocusDocument() => View.Focus();

    /// <summary>Writes the texts of the status bar (called after every change of the document).</summary>
    public virtual void UpdateStatus(IOfficeStatusBar statusBar)
    {
    }

    /// <summary>The window lets go of this editor (another document came in its place): unsubscribe from the document.</summary>
    public virtual void Detach()
    {
    }
}
