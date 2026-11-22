using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixOffice.Core.Models;

namespace ThixMail.Core;

/// <summary>
/// A rule of this computer, like Outlook's simple rules: the conditions (all that are set must hold) and the actions. Kept as an item of the settings
/// store (collection <c>mail-rules</c>), ready for ThixCloud's general sync like the signatures. The folder of "Verschieben" is named by its path in
/// the rule's account (a rule that moves belongs to one account).
/// </summary>
public sealed record MailRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = "";

    public bool IsEnabled { get; init; } = true;

    /// <summary>Where the rule stands among the rules (they run in this order).</summary>
    public int Order { get; init; }

    /// <summary>The account it applies to (null = every account).</summary>
    public string? AccountId { get; init; }

    // ---- conditions ----

    /// <summary>The sender's name or address contains this.</summary>
    public string? FromContains { get; init; }

    /// <summary>A recipient (An or Cc) – name or address – contains this.</summary>
    public string? ToContains { get; init; }

    public string? SubjectContains { get; init; }

    public bool HasAttachment { get; init; }

    // ---- actions ----

    /// <summary>"In Ordner verschieben": the folder's path in the rule's account.</summary>
    public string? MoveToFolder { get; init; }

    public bool MarkRead { get; init; }

    /// <summary>"Kategorisieren" with this category.</summary>
    public string? Category { get; init; }

    /// <summary>"Zur Nachverfolgung kennzeichnen".</summary>
    public bool Flag { get; init; }

    /// <summary>"Löschen": into Gelöschte Elemente.</summary>
    public bool Delete { get; init; }

    /// <summary>"Keine weiteren Regeln anwenden".</summary>
    public bool StopProcessing { get; init; }

    public bool HasCondition => !string.IsNullOrWhiteSpace(FromContains) || !string.IsNullOrWhiteSpace(ToContains) || !string.IsNullOrWhiteSpace(SubjectContains) || HasAttachment;

    public bool HasAction => MoveToFolder is { Length: > 0 } || MarkRead || Category is { Length: > 0 } || Flag || Delete;
}

/// <summary>What the rules decided for one message: flags to set, categories to add, where it goes (a folder, the trash) – applied by the caller.</summary>
public sealed record RuleOutcome(long MessageId, bool MarkRead, bool Flag, IReadOnlyList<string> Categories, long? MoveTo, bool Delete, IReadOnlyList<string> Rules)
{
    public bool IsEmpty => !MarkRead && !Flag && Categories.Count == 0 && MoveTo is null && !Delete;
}

/// <summary>
/// The client-side rules (business rules, UI-free): validation, matching and the plan of actions for a list of messages. Every matching rule runs in
/// order (read, flag and categories add up) until a rule says "Keine weiteren Regeln anwenden"; the first rule that moves or deletes decides where the
/// message goes and ends the processing (the message has left the folder). Applied by <see cref="MailSession.ApplyRules"/> ("Regeln jetzt
/// anwenden", one undoable step) and by the document on arrival in a Posteingang (before the notification).
/// </summary>
public static class MailRules
{
    /// <summary>Checks a rule; the first problem as a <see cref="BusinessException"/> at its field.</summary>
    public static MailRule Validate(MailRule rule, MailStore store)
    {
        var name = rule.Name.Trim();
        if (name.Length == 0)
        {
            throw new BusinessException("Bitte geben Sie der Regel einen Namen.", nameof(MailRule.Name));
        }

        var normalized = rule with
        {
            Name = name,
            FromContains = Clean(rule.FromContains),
            ToContains = Clean(rule.ToContains),
            SubjectContains = Clean(rule.SubjectContains),
            Category = Clean(rule.Category),
            MoveToFolder = Clean(rule.MoveToFolder),
        };
        if (!normalized.HasCondition)
        {
            throw new BusinessException("Bitte wählen Sie mindestens eine Bedingung (Absender, Empfänger, Betreff oder Anlage).", "Conditions");
        }

        if (!normalized.HasAction)
        {
            throw new BusinessException("Bitte wählen Sie mindestens eine Aktion.", "Actions");
        }

        if (normalized.MoveToFolder is not null && normalized.Delete)
        {
            throw new BusinessException("Eine Regel kann eine Nachricht verschieben oder löschen, nicht beides.", "Actions");
        }

        if (normalized.MoveToFolder is { } path)
        {
            if (normalized.AccountId is null)
            {
                throw new BusinessException("Eine Regel, die verschiebt, gilt für ein Konto: Bitte wählen Sie das Konto.", nameof(MailRule.AccountId));
            }

            if (store.Folders(normalized.AccountId).All(f => f.Path != path))
            {
                throw new BusinessException("Diesen Ordner gibt es in dem Konto nicht.", nameof(MailRule.MoveToFolder));
            }
        }

        if (normalized.Category is { } category && (category.Any(char.IsWhiteSpace) || category.StartsWith('$') || category.StartsWith('\\')))
        {
            throw new BusinessException("Ein Kategoriename darf keine Leerzeichen enthalten und nicht mit $ oder \\ beginnen.", nameof(MailRule.Category));
        }

        return normalized;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>All set conditions hold for the message (texts case-insensitive, as "enthält").</summary>
    public static bool Matches(MailRule rule, MessageHeader message)
    {
        if (!rule.IsEnabled || !rule.HasCondition || (rule.AccountId is { } account && account != message.AccountId))
        {
            return false;
        }

        if (rule.FromContains is { Length: > 0 } from && !Contains(message.From, from))
        {
            return false;
        }

        if (rule.ToContains is { Length: > 0 } to && !message.To.Concat(message.Cc).Any(a => Contains(a, to)))
        {
            return false;
        }

        if (rule.SubjectContains is { Length: > 0 } subject && !message.Subject.Contains(subject, StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        return !rule.HasAttachment || message.HasAttachments;
    }

    private static bool Contains(MailAddress address, string text) =>
        address.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase) || address.Address.Contains(text, StringComparison.OrdinalIgnoreCase);

    /// <summary>What the rules do to each message (messages no rule touches are left out).</summary>
    public static IReadOnlyList<RuleOutcome> Plan(IReadOnlyList<MailRule> rules, IReadOnlyList<MessageHeader> messages, MailStore store)
    {
        var ordered = rules.Where(r => r.IsEnabled).OrderBy(r => r.Order).ToList();
        var outcomes = new List<RuleOutcome>();
        foreach (var message in messages)
        {
            bool read = false, flag = false, delete = false;
            long? moveTo = null;
            var categories = new List<string>();
            var applied = new List<string>();
            foreach (var rule in ordered.Where(r => Matches(r, message)))
            {
                applied.Add(rule.Name);
                read |= rule.MarkRead;
                flag |= rule.Flag;
                if (rule.Category is { } category && !categories.Contains(category, StringComparer.OrdinalIgnoreCase) && !message.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
                {
                    categories.Add(category);
                }

                if (rule.Delete)
                {
                    delete = true;
                    break;
                }

                if (rule.MoveToFolder is { } path && store.Folders(message.AccountId).FirstOrDefault(f => f.Path == path) is { } target && target.Id != message.FolderId)
                {
                    moveTo = target.Id;
                    break;
                }

                if (rule.StopProcessing)
                {
                    break;
                }
            }

            var outcome = new RuleOutcome(message.Id, read && !message.IsRead, flag && !message.IsFlagged, categories, moveTo, delete, applied);
            if (!outcome.IsEmpty)
            {
                outcomes.Add(outcome);
            }
        }

        return outcomes;
    }
}
