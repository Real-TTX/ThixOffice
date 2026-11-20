namespace ThixWrite.Core.Layout;

/// <summary>How tracked changes show (Word's "Markup" views in Überprüfen › Nachverfolgung).</summary>
public enum RevisionView
{
    /// <summary>"Alle Markups": insertions underlined and deletions struck through in the author's colour, change bars in the margin.</summary>
    AllMarkup,

    /// <summary>"Einfaches Markup": the final text with a change bar beside every changed line.</summary>
    SimpleMarkup,

    /// <summary>"Markup: keine": the final text.</summary>
    NoMarkup,

    /// <summary>"Original": the text before the changes.</summary>
    Original,
}
