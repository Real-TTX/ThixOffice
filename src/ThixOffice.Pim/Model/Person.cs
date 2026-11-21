using MatCloud.Contract.Contacts;

namespace ThixOffice.Pim.Model;

/// <summary>
/// A person (a contact) as ThixContacts and ThixMail know it: its key (the key of its item in the general sync, a GUID), its data – the contract's
/// <see cref="ContactData"/>, so every app and MatCloud read and write the same fields – and when it was changed last (the last writer wins).
/// </summary>
public sealed record Person(string Id, ContactData Data, DateTimeOffset UpdatedAt)
{
    /// <summary>The name to show (<see cref="ContactData.NameToShow"/>).</summary>
    public string Name => Data.NameToShow();

    /// <summary>The first e-mail address, or empty.</summary>
    public string Email => Data.Emails.FirstOrDefault()?.Value ?? string.Empty;

    /// <summary>The first phone number, or empty.</summary>
    public string Phone => Data.Phones.FirstOrDefault()?.Value ?? string.Empty;

    /// <summary>The initials for an avatar: the first letters of given and family name, else of the name to show ("?" for nothing).</summary>
    public string Initials
    {
        get
        {
            var parts = new[] { Data.GivenName.Trim(), Data.FamilyName.Trim() }.Where(p => p.Length > 0).ToList();
            if (parts.Count == 0)
            {
                parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).ToList();
            }

            var letters = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0])));
            return letters.Length == 0 ? "?" : letters;
        }
    }
}

/// <summary>A group of persons (Outlook's "Kontaktgruppe"): its key, its data (name, the keys of its members) and its time.</summary>
public sealed record PersonGroup(string Id, ContactGroupData Data, DateTimeOffset UpdatedAt)
{
    public string Name => Data.Name;
}
