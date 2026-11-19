using ThixShow.Core.Model;

namespace ThixShow.Core.Editing;

/// <summary>
/// What "Kopf- und Fußzeile" puts on slides: the date (updated automatically – a datetime field – or a fixed text), the slide number
/// (a slidenum field), a footer text; optionally not on title slides.
/// </summary>
public sealed record HeaderFooterSettings(
    bool Date = false,
    bool DateAutomatic = true,
    string FixedDate = "",
    bool SlideNumber = false,
    bool Footer = false,
    string FooterText = "",
    bool NotOnTitleSlide = false);

public sealed partial class EditingSession
{
    /// <summary>
    /// The header/footer settings of a slide as its placeholders show them (the dialog starts from these): which of date, slide number
    /// and footer it has, the footer's text, whether its date is a field.
    /// </summary>
    public HeaderFooterSettings HeaderFooterOf(Slide slide)
    {
        var footers = slide.Shapes.OfType<AutoShape>().Where(shape => shape.Placeholder is { IsFooterKind: true }).ToList();
        var date = footers.FirstOrDefault(shape => shape.Placeholder!.Type == PlaceholderType.DateAndTime);
        var footer = footers.FirstOrDefault(shape => shape.Placeholder!.Type == PlaceholderType.Footer);
        var dateIsField = date?.Text?.Paragraphs.SelectMany(paragraph => paragraph.Runs).Any(run => run is TextField { Type: { } type } && type.StartsWith("datetime", StringComparison.Ordinal)) ?? true;
        return new HeaderFooterSettings(
            Date: date is not null,
            DateAutomatic: dateIsField,
            FixedDate: date is not null && !dateIsField ? date.Text?.PlainText ?? "" : "",
            SlideNumber: footers.Any(shape => shape.Placeholder!.Type == PlaceholderType.SlideNumber),
            Footer: footer is not null,
            FooterText: footer?.Text?.PlainText ?? "");
    }

    /// <summary>
    /// Kopf- und Fußzeile › Übernehmen (the selected slides) or Für alle übernehmen: each slide gets the date, slide number and footer
    /// placeholders its layout offers for what is switched on (placed and formatted by the layout), and loses those switched off.
    /// One undo step.
    /// </summary>
    public void SetHeaderFooter(HeaderFooterSettings settings, IReadOnlyList<uint> slideIds) =>
        Apply("Kopf- und Fußzeile", presentation => slideIds.Aggregate(presentation, (current, id) => WithSlide(current, id, slide =>
        {
            var layout = current.LayoutOf(slide);
            var master = current.MasterOf(slide);
            var kept = slide.Shapes.Where(shape => shape.Placeholder is not { IsFooterKind: true }).ToList();
            var onTitle = layout.Type == "title" && settings.NotOnTitleSlide;
            var nextId = Math.Max(slide.MaxShapeId, 1) + 1;
            SlideElement? Make(PlaceholderType type, TextBody text)
            {
                var source = layout.Shapes.FirstOrDefault(shape => shape.Placeholder?.Type == type)
                             ?? master.Shapes.FirstOrDefault(shape => shape.Placeholder?.Type == type);
                if (source?.Placeholder is not { } placeholder)
                {
                    return null;
                }

                var shapeId = nextId++;
                return new AutoShape { Id = shapeId, Name = MasterFactory.PlaceholderName(placeholder, shapeId), Placeholder = placeholder with { HasCustomPrompt = false }, Text = text };
            }

            if (!onTitle)
            {
                if (settings.Date)
                {
                    var text = settings.DateAutomatic
                        ? FieldBody(new TextField(NewFieldId(), "datetime1", _clock().ToLocalTime().ToString("dd.MM.yyyy", System.Globalization.CultureInfo.GetCultureInfo("de-DE")), RunProperties.Empty))
                        : TextBody.FromText(settings.FixedDate);
                    kept.AddRange(Make(PlaceholderType.DateAndTime, text) is { } date ? [date] : []);
                }

                if (settings.Footer)
                {
                    kept.AddRange(Make(PlaceholderType.Footer, TextBody.FromText(settings.FooterText)) is { } footer ? [footer] : []);
                }

                if (settings.SlideNumber)
                {
                    kept.AddRange(Make(PlaceholderType.SlideNumber, FieldBody(new TextField(NewFieldId(), "slidenum", "‹Nr.›", RunProperties.Empty))) is { } number ? [number] : []);
                }
            }

            return slide with { Shapes = kept };
        })), _ => Selection with { Shapes = [], Text = null });

    private static TextBody FieldBody(TextField field) =>
        new(BodyProperties.Empty, ListStyle.Empty, [new TextParagraph(ParagraphProperties.Empty, [field])]);

    private static string NewFieldId() => "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
}
