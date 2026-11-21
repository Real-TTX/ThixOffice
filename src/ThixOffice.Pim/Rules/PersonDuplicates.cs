using MatCloud.Contract.Contacts;

namespace ThixOffice.Pim.Rules;

/// <summary>A field of a merge the person decides (whose value the merged contact takes): its id and its German name.</summary>
public sealed record MergeField(string Id, string Label);

/// <summary>
/// Duplicates and merging – the same rules as MatCloud's module Contacts (<c>ContactDuplicates</c> there):
/// - Two contacts are duplicates when their names are the same (folded: case, accents and spaces do not count; a name of at least 3 letters), or they share
///   an e-mail address (case does not count), or a phone number (the same digits, at least 7; +49 and 0049 are the same). Duplicates of duplicates form
///   one set.
/// - Merging keeps the first contact (its key, its groups) and takes, per field of <see cref="Fields"/>, the value of the contact the person chose – by
///   default the first that has one. E-mail addresses, phone numbers, addresses, web addresses and categories are joined (each once), a favourite stays a
///   favourite, notes that differ are joined with an empty line.
/// </summary>
public static class PersonDuplicates
{
    public static readonly IReadOnlyList<MergeField> Fields =
    [
        new("name", "Name"),
        new("display", "Anzeigename"),
        new("nickname", "Spitzname"),
        new("company", "Firma und Abteilung"),
        new("title", "Position"),
        new("birthday", "Geburtstag"),
        new("anniversary", "Jahrestag"),
        new("photo", "Bild"),
    ];

    /// <summary>The sets of duplicates (each at least two keys, in the order of <paramref name="contacts"/>), the largest first.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Find(IReadOnlyList<(string Key, ContactData Data)> contacts)
    {
        var parent = Enumerable.Range(0, contacts.Count).ToArray();
        int Root(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        void Join(int a, int b)
        {
            var ra = Root(a);
            var rb = Root(b);
            if (ra != rb)
            {
                parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
            }
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < contacts.Count; i++)
        {
            foreach (var key in Keys(contacts[i].Data))
            {
                if (seen.TryGetValue(key, out var other))
                {
                    Join(i, other);
                }
                else
                {
                    seen[key] = i;
                }
            }
        }

        return Enumerable.Range(0, contacts.Count)
            .GroupBy(Root)
            .Where(g => g.Count() > 1)
            .Select(g => (IReadOnlyList<string>)g.OrderBy(i => i).Select(i => contacts[i].Key).ToList())
            .OrderByDescending(s => s.Count)
            .ThenBy(s => contacts.Select(c => c.Key).ToList().IndexOf(s[0]))
            .ToList();
    }

    /// <summary>What makes a contact the same as another: "n:" its folded name, "e:" each e-mail address, "p:" the last 9 digits of each phone number.</summary>
    public static IEnumerable<string> Keys(ContactData contact)
    {
        var name = PersonRules.Fold(string.Join(' ', new[] { contact.GivenName, contact.MiddleName, contact.FamilyName }.Where(p => p.Trim().Length > 0)));
        if (name.Length == 0)
        {
            name = PersonRules.Fold(contact.DisplayName);
        }

        if (name.Count(char.IsLetter) >= 3)
        {
            yield return "n:" + name;
        }

        foreach (var email in contact.Emails.Select(e => e.Value.Trim().ToLowerInvariant()).Where(e => e.Length > 0).Distinct())
        {
            yield return "e:" + email;
        }

        foreach (var digits in contact.Phones.Select(p => PersonRules.PhoneDigits(p.Value)).Where(d => d.Length >= 7).Distinct())
        {
            // the national and the international form of one number end alike: 089 1234567 and +49 89 1234567
            yield return "p:" + (digits.Length > 9 ? digits[^9..] : digits);
        }
    }

    /// <summary>
    /// The contact that merging <paramref name="contacts"/> (the first is kept) gives; <paramref name="choices"/> names per field id of <see cref="Fields"/>
    /// the index of the contact whose value is taken (missing: the first that has one).
    /// </summary>
    public static ContactData Merge(IReadOnlyList<ContactData> contacts, IReadOnlyDictionary<string, int>? choices = null)
    {
        if (contacts.Count == 0)
        {
            throw new ArgumentException("nothing to merge", nameof(contacts));
        }

        ContactData Source(string field, Func<ContactData, bool> has)
        {
            if (choices is not null && choices.TryGetValue(field, out var index) && index >= 0 && index < contacts.Count)
            {
                return contacts[index];
            }

            return contacts.FirstOrDefault(has) ?? contacts[0];
        }

        var name = Source("name", c => (c.GivenName + c.FamilyName + c.MiddleName + c.Prefix + c.Suffix).Trim().Length > 0);
        var display = Source("display", c => c.DisplayName.Length > 0);
        var nickname = Source("nickname", c => c.Nickname.Length > 0);
        var company = Source("company", c => c.Company.Length > 0 || c.Department.Length > 0);
        var title = Source("title", c => c.JobTitle.Length > 0);
        var birthday = Source("birthday", c => c.Birthday.Length > 0);
        var anniversary = Source("anniversary", c => c.Anniversary.Length > 0);
        var photo = Source("photo", c => c.PhotoId.Length > 0);

        var notes = new List<string>();
        foreach (var text in contacts.Select(c => c.Notes.Trim()).Where(t => t.Length > 0))
        {
            if (!notes.Contains(text, StringComparer.Ordinal))
            {
                notes.Add(text);
            }
        }

        var merged = contacts[0] with
        {
            Prefix = name.Prefix,
            GivenName = name.GivenName,
            MiddleName = name.MiddleName,
            FamilyName = name.FamilyName,
            Suffix = name.Suffix,
            DisplayName = display.DisplayName,
            Nickname = nickname.Nickname,
            Company = company.Company,
            Department = company.Department,
            JobTitle = title.JobTitle,
            Birthday = birthday.Birthday,
            Anniversary = anniversary.Anniversary,
            PhotoId = photo.PhotoId,
            Notes = string.Join("\n\n", notes),
            Emails = Distinct(contacts.SelectMany(c => c.Emails), v => v.Value.Trim().ToLowerInvariant()),
            Phones = Distinct(contacts.SelectMany(c => c.Phones), v => PersonRules.PhoneDigits(v.Value)),
            Urls = Distinct(contacts.SelectMany(c => c.Urls), v => v.Value.Trim().TrimEnd('/').ToLowerInvariant()),
            Addresses = Distinct(contacts.SelectMany(c => c.Addresses), a => PersonRules.Fold(a.Street + "|" + a.PostalCode + "|" + a.City + "|" + a.Country)),
            Categories = Distinct(contacts.SelectMany(c => c.Categories), c => c.ToLowerInvariant()),
            IsFavorite = contacts.Any(c => c.IsFavorite),
        };
        return PersonRules.Normalize(merged);
    }

    /// <summary>What a field of <see cref="Fields"/> shows of a contact in the merge dialog (empty: nothing).</summary>
    public static string ValueOf(ContactData contact, string field) => field switch
    {
        "name" => string.Join(' ', new[] { contact.Prefix, contact.GivenName, contact.MiddleName, contact.FamilyName, contact.Suffix }.Where(p => p.Length > 0)),
        "display" => contact.DisplayName,
        "nickname" => contact.Nickname,
        "company" => string.Join(", ", new[] { contact.Company, contact.Department }.Where(p => p.Length > 0)),
        "title" => contact.JobTitle,
        "birthday" => contact.Birthday,
        "anniversary" => contact.Anniversary,
        "photo" => contact.PhotoId.Length > 0 ? "Bild" : string.Empty,
        _ => string.Empty,
    };

    private static List<T> Distinct<T>(IEnumerable<T> values, Func<T, string> key)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<T>();
        foreach (var value in values)
        {
            if (seen.Add(key(value)))
            {
                result.Add(value);
            }
        }

        return result;
    }
}
