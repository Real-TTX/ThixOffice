using Avalonia.Controls;

namespace ThixOffice.Desktop.Shell;

/// <summary>
/// The app's part of a document window: the control in the editor area for one document. ThixWrite's text editor,
/// ThixCalc's grid and ThixShow's slide canvas derive from it; the shell asks it for the focus, the status bar texts and
/// the zoom (the slider of the status bar and Ansicht › Zoom set <see cref="Zoom"/>).
/// </summary>
public abstract class OfficeEditor
{
    /// <summary>The smallest zoom in percent.</summary>
    public const double MinZoom = 10;

    /// <summary>The largest zoom in percent.</summary>
    public const double MaxZoom = 500;

    private double _zoom = 100;

    /// <summary>The zoom changed (by the status bar, a command or the editor itself, e.g. Strg + mouse wheel later).</summary>
    public event EventHandler<double>? ZoomChanged;

    /// <summary>The control in the editor area.</summary>
    public abstract Control View { get; }

    /// <summary>
    /// The zoom in percent, held to <see cref="MinZoom"/>–<see cref="MaxZoom"/> and rounded to whole percent. Setting it
    /// calls <see cref="OnZoomChanged"/> (the editor draws itself larger or smaller) and raises <see cref="ZoomChanged"/>.
    /// Fitting the page or its width comes with the real editors.
    /// </summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            var zoom = double.IsNaN(value) ? 100 : Math.Round(Math.Clamp(value, MinZoom, MaxZoom));
            if (zoom == _zoom)
            {
                return;
            }

            _zoom = zoom;
            OnZoomChanged(zoom);
            ZoomChanged?.Invoke(this, zoom);
        }
    }

    /// <summary>
    /// A warning the window shows once when the document comes into it (content of the file the editor keeps but cannot
    /// show), or null.
    /// </summary>
    public virtual string? OpeningWarning => null;

    /// <summary>
    /// The document's pages for a PDF ("Speichern unter" › PDF, "Als PDF speichern"), or null when the editor has none (then PDF is not
    /// offered). Read when the PDF is written, so it holds the document as it is at that moment.
    /// </summary>
    public virtual PdfSource? Pdf => null;

    /// <summary>A click on the percentage of the status bar: the editor's zoom dialog (the default goes back to 100 %).</summary>
    public virtual void RequestZoomLevel() => Zoom = 100;

    /// <summary>
    /// Saving is about to write <paramref name="path"/> (its type by the extension): the editor ends an edit in progress and may
    /// warn about what the type cannot keep (ThixCalc: CSV keeps one sheet's values). False cancels the save.
    /// </summary>
    public virtual Task<bool> ConfirmSaveAsync(string path) => Task.FromResult(true);

    /// <summary>Puts the keyboard into the document (after the backstage, the search, opening).</summary>
    public virtual void FocusDocument() => View.Focus();

    /// <summary>Writes the texts of the status bar (called after every change of the document).</summary>
    public virtual void UpdateStatus(OfficeStatusBar statusBar)
    {
    }

    /// <summary>The window lets go of this editor (another document came in its place): unsubscribe from the document.</summary>
    public virtual void Detach()
    {
    }

    /// <summary>Draws the document at <paramref name="zoom"/> percent (the default ignores it).</summary>
    protected virtual void OnZoomChanged(double zoom)
    {
    }
}
