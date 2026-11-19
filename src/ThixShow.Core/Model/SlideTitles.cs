namespace ThixShow.Core.Model;

/// <summary>The title a slide shows in lists (Gehe zu Folie, the outline, the presenter's navigator): its title placeholder's text.</summary>
public static class SlideTitles
{
    /// <summary>The first line of the slide's title, or "Folie N" when it has none.</summary>
    public static string TitleOf(Presentation presentation, Slide slide)
    {
        var title = ShapeTree.All(slide.Shapes).OfType<AutoShape>()
            .FirstOrDefault(shape => shape.Placeholder?.IsTitle == true && shape.Text is { IsEmpty: false });
        var text = title?.Text?.PlainText.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return string.IsNullOrEmpty(text) ? $"Folie {presentation.IndexOf(slide.Id) + 1}" : text;
    }
}
