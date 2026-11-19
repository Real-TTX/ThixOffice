using ThixOffice.Text.Skia;
using ThixWrite.Core.Formats.Docx;
using ThixWrite.Core.Layout;

namespace ThixWrite.Core.Tests.RealWorld;

/// <summary>
/// Draws every page of every corpus document (and of any .docx in <c>THIXWRITE_RENDER_EXTRA</c>) as PNG into the folder named by
/// <c>THIXWRITE_RENDER</c> – for looking at them as a Word user would. Does nothing without the variable.
/// </summary>
public sealed class CorpusRendering
{
    [Fact]
    public void Render_every_page_when_asked()
    {
        var target = Environment.GetEnvironmentVariable("THIXWRITE_RENDER");
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        Directory.CreateDirectory(target);
        var files = Directory.GetFiles(Corpus.Folder, "*.docx", SearchOption.AllDirectories).ToList();
        if (Environment.GetEnvironmentVariable("THIXWRITE_RENDER_EXTRA") is { Length: > 0 } extra && Directory.Exists(extra))
        {
            files.AddRange(Directory.GetFiles(extra, "*.docx"));
        }

        var filter = Environment.GetEnvironmentVariable("THIXWRITE_RENDER_FILTER");
        using var measurer = new SkiaTextMeasurer();
        foreach (var file in files.Where(file => string.IsNullOrEmpty(filter) || Path.GetFileName(file).Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            var document = DocxReader.Read(File.OpenRead(file));
            using var layout = new DocumentLayout(document, measurer);
            layout.Update();
            var name = Path.GetFileNameWithoutExtension(file);
            var notes = string.Join(", ", document.UnsupportedContent.Select(note => $"{note.Kind}×{note.Count}"));
            File.WriteAllText(Path.Combine(target, name + ".txt"), $"{layout.PageCount} pages; unsupported: {notes}\n");
            foreach (var page in layout.GetDisplayLists())
            {
                File.WriteAllBytes(Path.Combine(target, $"{name}-p{page.PageIndex + 1}.png"), SkiaPageRenderer.RenderPng(page, 1100, measurer));
            }
        }
    }
}
