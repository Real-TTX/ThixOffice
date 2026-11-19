namespace ThixShow.Core.Model;

/// <summary>
/// Font sizes scaled by a factor (Foliengröße › "Passend skalieren" shrinks text with the shapes, like PowerPoint): every explicit size
/// of runs, paragraph defaults, list styles, shapes in groups, table cells, and the master's text styles. Sizes stay at least 1 pt.
/// </summary>
public static class TextScaling
{
    public static RunProperties? Scale(RunProperties? run, double factor) =>
        run is { Size: { } size } ? run with { Size = Math.Max(100, (int)Math.Round(size * factor)) } : run;

    public static ParagraphProperties? Scale(ParagraphProperties? paragraph, double factor) =>
        paragraph is { DefaultRun: { } run } ? paragraph with { DefaultRun = Scale(run, factor) } : paragraph;

    public static ListStyle Scale(ListStyle style, double factor) =>
        style with { Default = Scale(style.Default, factor), Levels = [.. style.Levels.Select(level => Scale(level, factor))] };

    public static TextBody Scale(TextBody body, double factor) => body with
    {
        ListStyle = Scale(body.ListStyle, factor),
        Paragraphs = [.. body.Paragraphs.Select(paragraph => paragraph with
        {
            Properties = Scale(paragraph.Properties, factor)!,
            EndProperties = Scale(paragraph.EndProperties, factor),
            Runs = [.. paragraph.Runs.Select(run => Scale(run, factor))],
        })],
    };

    private static TextRun Scale(TextRun run, double factor) => run switch
    {
        TextSpan span => span with { RunProperties = Scale(span.RunProperties, factor)! },
        LineBreak lineBreak => lineBreak with { RunProperties = Scale(lineBreak.RunProperties, factor)! },
        TextField field => field with { RunProperties = Scale(field.RunProperties, factor)! },
        _ => run,
    };

    /// <summary>A shape's text (groups and tables included) scaled.</summary>
    public static SlideElement Scale(SlideElement shape, double factor) => shape switch
    {
        AutoShape { Text: { } text } auto => auto with { Text = Scale(text, factor) },
        GroupShape group => group with { Children = [.. group.Children.Select(child => Scale(child, factor))] },
        GraphicFrame { Table: { } table } frame => frame with
        {
            Table = table with { Rows = [.. table.Rows.Select(row => row with { Cells = [.. row.Cells.Select(cell => cell with { Text = Scale(cell.Text, factor) })] })] },
        },
        _ => shape,
    };

    public static MasterTextStyles Scale(MasterTextStyles styles, double factor) =>
        new(Scale(styles.Title, factor), Scale(styles.Body, factor), Scale(styles.Other, factor));
}
