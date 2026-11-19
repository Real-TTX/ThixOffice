using ThixOffice.Core.Documents;
using ThixOffice.Core.Models;
using ThixShow.Core.Editing;
using ThixShow.Core.Formats.Pptx;
using ThixShow.Core.Model;

namespace ThixShow.Core.Documents;

/// <summary>The file formats a <see cref="ShowDocument"/> reads and writes.</summary>
public enum ShowFileFormat
{
    Pptx,
    Pptm,
}

/// <summary>
/// The document of a ThixShow window: the presentation inside its <see cref="Session"/> (selection, commands, undo). Reading takes
/// .pptx/.pptm (macros are kept and never run); writing uses <see cref="SaveFormat"/> – "Speichern unter" as .pptx drops the macros
/// (the app asks first, like ThixCalc for .xlsx), as .pptm keeps them. Every finished command, undo and redo marks it unsaved.
/// </summary>
public sealed class ShowDocument : OfficeDocument
{
    public ShowDocument(string untitledName)
        : this(untitledName, MasterFactory.CreatePresentation())
    {
    }

    public ShowDocument(string untitledName, Presentation presentation)
        : base(untitledName)
    {
        Attach(presentation);
    }

    /// <summary>Raised when <see cref="Session"/> was replaced (a file was read into this document).</summary>
    public event EventHandler? ContentReplaced;

    /// <summary>Raised before the document is written (an open text editor commits first).</summary>
    public event EventHandler? Writing;

    public EditingSession Session { get; private set; } = null!;

    public Presentation Presentation => Session.Presentation;

    public ShowFileFormat LoadedFormat { get; private set; } = ShowFileFormat.Pptx;

    public ShowFileFormat SaveFormat { get; set; } = ShowFileFormat.Pptx;

    /// <summary>True when the presentation carries macros (they are kept, never run).</summary>
    public bool HasMacros => Presentation.IsMacroEnabled;

    /// <summary>What the opened file had that ThixShow keeps but does not show (German messages for the warning bar).</summary>
    public IReadOnlyList<UnsupportedContent> UnsupportedContent => Presentation.UnsupportedContent;

    public override bool CanUndo => Session.CanUndo;

    public override bool CanRedo => Session.CanRedo;

    public override void Undo() => Session.Undo();

    public override void Redo() => Session.Redo();

    public static ShowFileFormat? FormatOf(string extension) => extension.ToLowerInvariant() switch
    {
        ".pptx" => ShowFileFormat.Pptx,
        ".pptm" => ShowFileFormat.Pptm,
        _ => null,
    };

    /// <summary>Puts in a presentation without an undo step and without an unsaved change (samples, templates).</summary>
    public void Load(Presentation presentation)
    {
        Attach(presentation);
        RaiseChanged();
    }

    protected override void Read(Stream stream)
    {
        var presentation = PptxReader.Read(stream);
        LoadedFormat = presentation.IsMacroEnabled ? ShowFileFormat.Pptm : ShowFileFormat.Pptx;
        SaveFormat = LoadedFormat;
        Attach(presentation);
    }

    protected override void Write(Stream stream) => Write(stream, SaveFormat);

    protected override void Write(Stream stream, DocumentType type) => Write(stream, FormatOf(type.Extension) ?? SaveFormat);

    protected override void OnSaved(DocumentType type) => SaveFormat = FormatOf(type.Extension) ?? SaveFormat;

    private void Write(Stream stream, ShowFileFormat format)
    {
        Writing?.Invoke(this, EventArgs.Empty);
        PptxWriter.Write(Presentation with { IsMacroEnabled = format == ShowFileFormat.Pptm }, stream);
    }

    private void Attach(Presentation presentation)
    {
        if (Session is not null)
        {
            Session.PresentationChanged -= OnPresentationChanged;
        }

        Session = new EditingSession(presentation);
        Session.PresentationChanged += OnPresentationChanged;
        ContentReplaced?.Invoke(this, EventArgs.Empty);
    }

    private void OnPresentationChanged(object? sender, PresentationChangedEventArgs e) => OnContentChanged();
}
