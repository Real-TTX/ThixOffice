using System.Globalization;
using System.Text;
using MatCloud.Contract.Contacts;

namespace ThixOffice.Pim.Rules;

/// <summary>One problem of a contact: the German message and the field it belongs to (<c>GivenName</c>, <c>Emails[1]</c>).</summary>
public sealed record PersonProblem(string Message, string Field);

/// <summary>
/// The rules of a contact – the same here and in MatCloud's module Contacts (<c>ContactRules</c> there), held together by the shared fixtures:
/// what is cleaned before it is kept (<see cref="Normalize"/>), what is refused (<see cref="Validate"/>), how contacts are compared and sorted
/// (<see cref="SortKey"/>, <see cref="Fold"/>, <see cref="PhoneDigits"/>).
/// </summary>
public static class PersonRules
{
    public const int MaxCategoryLength = 100;

    private const string PhoneCharacters = "0123456789+-()/. #*x";

    /// <summary>
    /// The contact as it is kept: every text trimmed (notes keep their inner lines; line ends become "\n"), empty values and addresses left out,
    /// web addresses without a scheme get "https://", categories once each (the first spelling wins), labels in small letters.
    /// </summary>
    public static ContactData Normalize(ContactData contact)
    {
        static string T(string? text) => (text ?? string.Empty).Trim();
        static IReadOnlyList<ContactValue> Values(IEnumerable<ContactValue>? values, Func<string, string>? clean = null)
            => (values ?? []).Select(v => new ContactValue(Label(v.Label), (clean ?? T)(v.Value))).Where(v => v.Value.Length > 0).ToList();

        var categories = new List<string>();
        foreach (var category in (contact.Categories ?? []).Select(T).Where(c => c.Length > 0))
        {
            if (!categories.Contains(category, StringComparer.OrdinalIgnoreCase))
            {
                categories.Add(category);
            }
        }

        return contact with
        {
            Prefix = T(contact.Prefix),
            GivenName = T(contact.GivenName),
            MiddleName = T(contact.MiddleName),
            FamilyName = T(contact.FamilyName),
            Suffix = T(contact.Suffix),
            Nickname = T(contact.Nickname),
            DisplayName = T(contact.DisplayName),
            Company = T(contact.Company),
            Department = T(contact.Department),
            JobTitle = T(contact.JobTitle),
            Emails = Values(contact.Emails),
            Phones = Values(contact.Phones),
            Urls = Values(contact.Urls, CleanUrl),
            Addresses = (contact.Addresses ?? []).Select(a => a with
            {
                Label = Label(a.Label),
                Street = string.Join('\n', Lines(a.Street)),
                PostalCode = T(a.PostalCode),
                City = T(a.City),
                Region = T(a.Region),
                Country = T(a.Country),
            }).Where(a => !a.IsEmpty).ToList(),
            Birthday = T(contact.Birthday),
            Anniversary = T(contact.Anniversary),
            Notes = (contact.Notes ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim(),
            PhotoId = T(contact.PhotoId).ToLowerInvariant(),
            Categories = categories,
        };
    }

    /// <summary>What is wrong with a (normalized) contact; empty when nothing.</summary>
    public static IReadOnlyList<PersonProblem> Validate(ContactData contact)
    {
        var problems = new List<PersonProblem>();
        if (!contact.HasName)
        {
            problems.Add(new("Bitte einen Namen, eine Firma oder eine E-Mail-Adresse eingeben.", nameof(ContactData.GivenName)));
        }

        foreach (var (value, field) in new[]
                 {
                     (contact.Prefix, nameof(ContactData.Prefix)), (contact.GivenName, nameof(ContactData.GivenName)),
                     (contact.MiddleName, nameof(ContactData.MiddleName)), (contact.FamilyName, nameof(ContactData.FamilyName)),
                     (contact.Suffix, nameof(ContactData.Suffix)), (contact.Nickname, nameof(ContactData.Nickname)),
                     (contact.DisplayName, nameof(ContactData.DisplayName)), (contact.Company, nameof(ContactData.Company)),
                     (contact.Department, nameof(ContactData.Department)), (contact.JobTitle, nameof(ContactData.JobTitle)),
                 })
        {
            if (value.Length > ContactsContract.MaxNameLength)
            {
                problems.Add(new($"Höchstens {ContactsContract.MaxNameLength} Zeichen.", field));
            }
        }

        CheckCount(contact.Emails.Count, nameof(ContactData.Emails), "E-Mail-Adressen", problems);
        CheckCount(contact.Phones.Count, nameof(ContactData.Phones), "Telefonnummern", problems);
        CheckCount(contact.Addresses.Count, nameof(ContactData.Addresses), "Adressen", problems);
        CheckCount(contact.Urls.Count, nameof(ContactData.Urls), "Webadressen", problems);
        CheckCount(contact.Categories.Count, nameof(ContactData.Categories), "Kategorien", problems);

        for (var i = 0; i < contact.Emails.Count; i++)
        {
            var value = contact.Emails[i].Value;
            if (value.Length > ContactsContract.MaxValueLength || !IsEmail(value))
            {
                problems.Add(new($"„{Short(value)}“ ist keine E-Mail-Adresse.", $"Emails[{i}]"));
            }
        }

        for (var i = 0; i < contact.Phones.Count; i++)
        {
            var value = contact.Phones[i].Value;
            if (value.Length > ContactsContract.MaxValueLength || !IsPhone(value))
            {
                problems.Add(new($"„{Short(value)}“ ist keine Telefonnummer: Ziffern, Leerzeichen und + - ( ) / . erlaubt.", $"Phones[{i}]"));
            }
        }

        for (var i = 0; i < contact.Urls.Count; i++)
        {
            var value = contact.Urls[i].Value;
            if (value.Length > ContactsContract.MaxValueLength || !IsUrl(value))
            {
                problems.Add(new($"„{Short(value)}“ ist keine Webadresse (wie https://example.com).", $"Urls[{i}]"));
            }
        }

        for (var i = 0; i < contact.Addresses.Count; i++)
        {
            var a = contact.Addresses[i];
            if (new[] { a.Street, a.PostalCode, a.City, a.Region, a.Country }.Any(p => p.Length > ContactsContract.MaxValueLength))
            {
                problems.Add(new($"Eine Zeile der Adresse hat höchstens {ContactsContract.MaxValueLength} Zeichen.", $"Addresses[{i}]"));
            }
        }

        if (contact.Birthday.Length > 0 && !ContactsContract.IsValidDate(contact.Birthday))
        {
            problems.Add(new("Bitte ein gültiges Datum eingeben.", nameof(ContactData.Birthday)));
        }

        if (contact.Anniversary.Length > 0 && !ContactsContract.IsValidDate(contact.Anniversary))
        {
            problems.Add(new("Bitte ein gültiges Datum eingeben.", nameof(ContactData.Anniversary)));
        }

        if (contact.Notes.Length > ContactsContract.MaxNotesLength)
        {
            problems.Add(new($"Die Notizen haben höchstens {ContactsContract.MaxNotesLength / 1000} Tausend Zeichen.", nameof(ContactData.Notes)));
        }

        if (contact.PhotoId.Length > 0 && !ContactsContract.IsValidPhotoId(contact.PhotoId))
        {
            problems.Add(new("Das Bild ist unbekannt.", nameof(ContactData.PhotoId)));
        }

        foreach (var category in contact.Categories)
        {
            if (CategoryProblem(category) is { } problem)
            {
                problems.Add(new(problem, nameof(ContactData.Categories)));
                break;
            }
        }

        return problems;
    }

    /// <summary>What is wrong with the name of a category or a group, or null.</summary>
    public static string? CategoryProblem(string name)
    {
        if (name.Length == 0)
        {
            return "Bitte einen Namen eingeben.";
        }

        if (name.Length > MaxCategoryLength)
        {
            return $"Ein Name hat höchstens {MaxCategoryLength} Zeichen.";
        }

        return name.IndexOfAny([',', ';']) >= 0 || name.Any(char.IsControl) ? "Ein Name hat kein Komma und kein Semikolon." : null;
    }

    /// <summary>Something@something.something, without spaces.</summary>
    public static bool IsEmail(string value)
    {
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return false;
        }

        var domain = value[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        return dot > 0 && dot < domain.Length - 1 && !domain.Contains("..", StringComparison.Ordinal);
    }

    /// <summary>Digits with + - ( ) / . # * and spaces, an "x" before an extension; at least one digit.</summary>
    public static bool IsPhone(string value)
        => value.Any(char.IsAsciiDigit) && value.ToLowerInvariant().All(c => PhoneCharacters.Contains(c, StringComparison.Ordinal));

    /// <summary>An absolute http or https address with a host.</summary>
    public static bool IsUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host.Length > 0;

    /// <summary>"example.com" → "https://example.com"; anything with a scheme stays.</summary>
    public static string CleanUrl(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 || text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text;
    }

    /// <summary>A label in small letters; empty → "other".</summary>
    public static string Label(string? label)
    {
        var text = (label ?? string.Empty).Trim().ToLowerInvariant();
        return text.Length == 0 ? ContactLabels.Other : text;
    }

    /// <summary>The digits of a phone number (a leading + kept as "00": +49 and 0049 are the same).</summary>
    public static string PhoneDigits(string value)
    {
        var digits = new StringBuilder();
        var text = value.Trim();
        var x = text.IndexOf('x', StringComparison.OrdinalIgnoreCase);
        if (x > 0)
        {
            text = text[..x];
        }

        if (text.StartsWith('+'))
        {
            digits.Append("00");
        }

        foreach (var c in text.Where(char.IsAsciiDigit))
        {
            digits.Append(c);
        }

        return digits.ToString();
    }

    /// <summary>
    /// A text for comparing and searching: small letters, accents and umlauts folded (ä → a, ß → ss), runs of white space as one space. The same on every
    /// machine (no culture data is needed: MatCloud runs with invariant globalization, and both must agree).
    /// </summary>
    public static string Fold(string? text)
    {
        var sb = new StringBuilder((text ?? string.Empty).Length);
        var space = false;
        foreach (var c in (text ?? string.Empty).Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                space = sb.Length > 0;
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            var lower = char.ToLowerInvariant(c);
            switch (lower)
            {
                case 'ß':
                    sb.Append("ss");
                    continue;
                case 'æ':
                    sb.Append("ae");
                    continue;
                case 'œ':
                    sb.Append("oe");
                    continue;
            }

            var at = Accented.IndexOf(lower, StringComparison.Ordinal);
            sb.Append(at >= 0 ? Plain[at] : lower);
        }

        return sb.ToString();
    }

    /// <summary>Letters with accents and their plain letter (by position): no culture data or Unicode tables needed, the same on every machine.</summary>
    private const string Accented = "àáâãäåāăąçćĉċčďđèéêëēĕėęěĝğġģĥħìíîïĩīĭįıĵķĺļľŀłñńņňòóôõöøōŏőŕŗřśŝşšţťŧùúûüũūŭůűųŵýÿŷźżž";

    private const string Plain = "aaaaaaaaacccccddeeeeeeeeegggghhiiiiiiiiijklllllnnnnoooooooooorrrssssttttuuuuuuuuuuwyyyzzz";

    /// <summary>The key a list sorts by: "Nachname, Vorname" or "Vorname Nachname" (the name to show when the parts are empty), then the company, folded.</summary>
    public static string SortKey(ContactData contact, PersonSort sort) => sort switch
    {
        PersonSort.FamilyName => Fold(First(contact.FamilyName, contact.GivenName, contact.NameToShow()) + " " + contact.GivenName + " " + contact.MiddleName),
        PersonSort.Company => Fold(First(contact.Company, contact.NameToShow()) + " " + contact.NameToShow()),
        _ => Fold(First(contact.GivenName, contact.NameToShow()) + " " + contact.MiddleName + " " + contact.FamilyName),
    };

    /// <summary>The letter of the alphabetical index a contact stands under ("#" for a digit or anything else).</summary>
    public static string IndexLetter(ContactData contact, PersonSort sort)
    {
        var key = SortKey(contact, sort);
        return key.Length > 0 && key[0] is >= 'a' and <= 'z' ? char.ToUpperInvariant(key[0]).ToString() : "#";
    }

    /// <summary>"Familie, Vorname" when the list is sorted by the family name, else the name to show.</summary>
    public static string ListName(ContactData contact, PersonSort sort)
    {
        if (sort == PersonSort.FamilyName && contact.FamilyName.Length > 0 && contact.DisplayName.Length == 0)
        {
            var given = string.Join(' ', new[] { contact.GivenName, contact.MiddleName }.Where(p => p.Length > 0));
            return given.Length == 0 ? contact.FamilyName : contact.FamilyName + ", " + given;
        }

        return contact.NameToShow();
    }

    /// <summary>Everything a search looks in, folded.</summary>
    public static string SearchText(ContactData c)
        => Fold(string.Join(' ', new[] { c.Prefix, c.GivenName, c.MiddleName, c.FamilyName, c.Suffix, c.Nickname, c.DisplayName, c.Company, c.Department, c.JobTitle }
            .Concat(c.Emails.Select(e => e.Value)).Concat(c.Phones.Select(p => p.Value)).Concat(c.Categories)
            .Concat(c.Addresses.SelectMany(a => new[] { a.Street, a.PostalCode, a.City, a.Country }))));

    /// <summary>True when every word of the search is in the contact (phone numbers also by their digits).</summary>
    public static bool Matches(ContactData contact, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var text = SearchText(contact);
        var digits = string.Concat(contact.Phones.Select(p => " " + PhoneDigits(p.Value)));
        return Fold(search).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(w => text.Contains(w, StringComparison.Ordinal) || (w.All(char.IsAsciiDigit) && digits.Contains(w, StringComparison.Ordinal)));
    }

    private static string First(params string[] texts) => texts.FirstOrDefault(t => t.Trim().Length > 0)?.Trim() ?? string.Empty;

    private static IEnumerable<string> Lines(string? text)
        => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    private static void CheckCount(int count, string field, string what, List<PersonProblem> problems)
    {
        if (count > ContactsContract.MaxValues)
        {
            problems.Add(new($"Höchstens {ContactsContract.MaxValues} {what}.", field));
        }
    }

    private static string Short(string value) => value.Length <= 40 ? value : value[..40] + "…";
}

/// <summary>How a list of contacts is sorted (Outlook: "Vorname", "Nachname", "Firma").</summary>
public enum PersonSort
{
    GivenName,
    FamilyName,
    Company,
}
