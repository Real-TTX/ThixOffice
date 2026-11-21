using MatCloud.Contract.Contacts;
using ThixOffice.Pim.Model;
using ThixOffice.Pim.Rules;

namespace ThixOffice.Pim.Store;

/// <summary>
/// The persons and groups of a store: the items of the collections <see cref="ContactsContract.Contacts"/> and <see cref="ContactsContract.Groups"/>
/// read as <see cref="Person"/> / <see cref="PersonGroup"/> (unreadable data is left out, never thrown) and written back as items. ThixContacts' business
/// layer works on these; ThixMail's address field asks <see cref="Suggest"/>.
/// </summary>
public static class PersonItems
{
    public static IReadOnlyList<Person> People(PimStore store)
        => store.Items(ContactsContract.Contacts)
            .Select(item => ContactsContract.ReadContact(item.Data) is { } data ? new Person(item.Key, PersonRules.Normalize(data), item.UpdatedAt) : null)
            .OfType<Person>()
            .ToList();

    public static IReadOnlyList<PersonGroup> Groups(PimStore store)
        => store.Items(ContactsContract.Groups)
            .Select(item => ContactsContract.ReadGroup(item.Data) is { } data
                ? new PersonGroup(item.Key, data with { Name = data.Name ?? string.Empty, Members = data.Members ?? [] }, item.UpdatedAt)
                : null)
            .OfType<PersonGroup>()
            .ToList();

    /// <summary>Writes persons and groups (changes of this computer) and deletes keys, in one transaction.</summary>
    public static void Write(PimStore store, IEnumerable<Person>? people = null, IEnumerable<PersonGroup>? groups = null,
        IEnumerable<string>? deletedPeople = null, IEnumerable<string>? deletedGroups = null)
    {
        var changes = new List<(string, string, string?)>();
        changes.AddRange((people ?? []).Select(p => (ContactsContract.Contacts, p.Id, (string?)ContactsContract.Serialize(p.Data))));
        changes.AddRange((groups ?? []).Select(g => (ContactsContract.Groups, g.Id, (string?)ContactsContract.Serialize(g.Data))));
        changes.AddRange((deletedPeople ?? []).Select(id => (ContactsContract.Contacts, id, (string?)null)));
        changes.AddRange((deletedGroups ?? []).Select(id => (ContactsContract.Groups, id, (string?)null)));
        if (changes.Count > 0)
        {
            store.Write(changes);
        }
    }

    /// <summary>
    /// Addresses for a field "An": persons whose name or e-mail address starts a word with <paramref name="text"/>, favourites first, each address
    /// once ("Anna Müller &lt;anna@example.de&gt;").
    /// </summary>
    public static IReadOnlyList<(Person Person, string Email)> Suggest(PimStore store, string text, int max = 8)
    {
        var folded = PersonRules.Fold(text);
        if (folded.Length == 0)
        {
            return [];
        }

        return People(store)
            .SelectMany(p => p.Data.Emails.Select(e => (Person: p, Email: e.Value)))
            .Where(pair => Starts(PersonRules.Fold(pair.Person.Name), folded) || pair.Email.StartsWith(text.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Person.Data.IsFavorite)
            .ThenBy(pair => PersonRules.Fold(pair.Person.Name), StringComparer.Ordinal)
            .DistinctBy(pair => pair.Email.ToLowerInvariant())
            .Take(max)
            .ToList();

        static bool Starts(string name, string prefix) => name.StartsWith(prefix, StringComparison.Ordinal) || name.Contains(" " + prefix, StringComparison.Ordinal);
    }
}
