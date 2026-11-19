using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Formats.Html;

namespace ThixCalc.Editor;

/// <summary>
/// The clipboard of a ThixCalc window. Kopieren / Ausschneiden put three formats on it: ThixCalc's own (a token that names
/// the copied cells of this process: values, formulas with their references, styles, merges, widths – pasted exactly, also
/// into another window), tab-separated text (every other program) and an HTML table (Word, mails, browsers keep the look).
/// Einfügen takes ThixCalc's own when it is there, else an HTML table of another program (its texts), else text (tab- or
/// line-separated; each field read like typed input). The copied range "marches" until Esc, an input or a paste of a cut.
/// </summary>
internal sealed class CalcClipboard(CalcEditor editor)
{
    private static readonly DataFormat<byte[]> OwnFormat = DataFormat.CreateBytesApplicationFormat("ThixCalc.Cells");
    private static ClipboardContent? s_content;
    private static string s_token = string.Empty;
    private static (ThixOffice.Charts.Chart Chart, double Width, double Height, string Name)? s_chart;

    /// <summary>Cells of ThixCalc are waiting to be pasted (Inhalte einfügen needs them).</summary>
    public bool HasOwnContent => s_content is not null;

    public async Task CopyAsync(bool cut)
    {
        if (!editor.Editing.Commit())
        {
            return;
        }

        var session = editor.Session;
        if (session.SelectedChart is { } chart)
        {
            await CopyChartAsync(chart, cut);
            return;
        }

        if (session.SelectedRanges.Count > 1)
        {
            throw new ThixOffice.Core.Models.BusinessException("Dieser Befehl kann nicht für eine Mehrfachauswahl verwendet werden.");
        }

        var content = cut ? session.Cut() : session.Copy();
        s_content = content;
        s_chart = null;
        s_token = Guid.NewGuid().ToString("N");
        editor.Surface.CopiedRange = content.Range;
        if (Clipboard is not { } clipboard)
        {
            return;
        }

        var html = HtmlTables.Export(content.Sheet, content.Range, session.Locale);
        var item = new DataTransferItem();
        item.Set(OwnFormat, Encoding.UTF8.GetBytes(s_token));
        item.SetText(content.Text);
        item.Set(DataFormat.CreateBytesPlatformFormat(OperatingSystem.IsMacOS() ? "public.html" : "HTML Format"),
            Encoding.UTF8.GetBytes(OperatingSystem.IsMacOS() ? html : HtmlTables.WrapForClipboard(html)));
        var transfer = new DataTransfer();
        transfer.Add(item);
        await clipboard.SetDataAsync(transfer);
    }

    /// <summary>Einfügen (all of it, or what <paramref name="options"/> say); <paramref name="textOnly"/>: only the text.</summary>
    public async Task PasteAsync(PasteOptions? options = null, bool textOnly = false)
    {
        if (!editor.Editing.Commit())
        {
            return;
        }

        var session = editor.Session;
        var clipboard = Clipboard;
        if (!textOnly && s_chart is { } chart && (clipboard is null || await HasOwnTokenAsync(clipboard)))
        {
            // A chart goes to the active cell in its size (a copy of its data references: Excel's way within a workbook).
            var layout = editor.LayoutOf(session.ActiveSheet);
            var anchor = ThixCalc.Core.Layout.ChartLayer.AnchorAt(layout, session.ActiveCell, chart.Width, chart.Height);
            session.PasteChart(chart.Chart, anchor);
            return;
        }

        if (!textOnly && s_content is { } content && (clipboard is null || await HasOwnTokenAsync(clipboard)))
        {
            session.Paste(content, options);
            if (content.IsCut)
            {
                s_content = null;
                editor.Surface.CopiedRange = null;
            }

            return;
        }

        if (clipboard is null)
        {
            return;
        }

        if (!textOnly && await TryGetHtmlAsync(clipboard) is { } html && HtmlTables.Import(html) is { Count: > 0 } rows)
        {
            session.PasteText(HtmlTables.ToTabText(rows));
            return;
        }

        if (await clipboard.TryGetTextAsync() is { Length: > 0 } text)
        {
            session.PasteText(text);
        }
    }

    /// <summary>Kopieren / Ausschneiden of a selected chart: ThixCalc's token and the chart's name as text.</summary>
    private async Task CopyChartAsync(ThixCalc.Core.Model.SheetChart chart, bool cut)
    {
        var (width, height) = editor.ChartSize(chart);
        s_chart = (chart.Chart, width, height, chart.Name);
        s_content = null;
        s_token = Guid.NewGuid().ToString("N");
        editor.Surface.CopiedRange = null;
        if (cut)
        {
            editor.Session.DeleteChart(chart.Id);
        }

        if (Clipboard is not { } clipboard)
        {
            return;
        }

        var item = new DataTransferItem();
        item.Set(OwnFormat, Encoding.UTF8.GetBytes(s_token));
        item.SetText(chart.Name);
        var transfer = new DataTransfer();
        transfer.Add(item);
        await clipboard.SetDataAsync(transfer);
    }

    private static async Task<bool> HasOwnTokenAsync(IClipboard clipboard)
    {
        try
        {
            return await clipboard.TryGetValueAsync(OwnFormat) is { Length: > 0 } bytes && Encoding.UTF8.GetString(bytes) == s_token;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private static async Task<string?> TryGetHtmlAsync(IClipboard clipboard)
    {
        var format = DataFormat.CreateBytesPlatformFormat(OperatingSystem.IsMacOS() ? "public.html" : "HTML Format");
        try
        {
            return await clipboard.TryGetValueAsync(format) is { Length: > 0 } bytes ? Encoding.UTF8.GetString(bytes) : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private IClipboard? Clipboard => TopLevel.GetTopLevel(editor.View)?.Clipboard ?? editor.Window.Clipboard;
}
