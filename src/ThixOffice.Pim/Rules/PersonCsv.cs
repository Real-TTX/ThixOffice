using System.Globalization;
using System.Text;
using MatCloud.Contract.Contacts;

namespace ThixOffice.Pim.Rules;

/// <summary>A field of a contact a column of a CSV file can fill: its id (the same in ThixContacts), its German name (the header written) and the other names it is known by.</summary>
public sealed record CsvField(string Id, string Header, params string[] Aliases);

/// <summary>A CSV file read: its columns (the first line) and its rows (as many cells as there are columns).</summary>
public sealed record CsvTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, char Separator);

/// <summary>
/// Contacts as CSV (own code): reading any CSV (separator ; , or tab found in the first line, quotes with "" inside, line breaks in quotes, UTF-8 with or
/// without BOM, else Latin-1), the columns matched to fields by their names (German and English Outlook, Google; <see cref="AutoMap"/>) and changed by the
/// person (the mapping), and writing in Outlook's German columns, one column per field (<see cref="Fields"/>). The same rules as MatCloud's
/// module Contacts (<c>ContactCsv</c> there); the shared fixtures hold both to the same results. CSV has one column per kind of value: a second work e-mail address goes into
/// "E-Mail 2", a third into "E-Mail 3", more are not written.
/// </summary>
public static class PersonCsv
{
    /// <summary>The field id that leaves a column out.</summary>
    public const string Ignore = "";

    public static readonly IReadOnlyList<CsvField> Fields =
    [
        new("prefix", "Anrede", "Title", "Name Prefix", "Titel"),
        new("given", "Vorname", "First Name", "Given Name"),
        new("middle", "Weitere Vornamen", "Middle Name", "Additional Name", "Zweiter Vorname"),
        new("family", "Nachname", "Last Name", "Family Name"),
        new("suffix", "Suffix", "Name Suffix", "Namenszusatz"),
        new("nickname", "Spitzname", "Nickname"),
        new("display", "Anzeigename", "Display Name", "Name", "Speichern unter"),
        new("company", "Firma", "Company", "Organization 1 - Name", "Organisation"),
        new("department", "Abteilung", "Department", "Organization 1 - Department"),
        new("title", "Position", "Job Title", "Organization 1 - Title", "Berufsbezeichnung"),
        new("email-1", "E-Mail-Adresse", "E-mail Address", "E-Mail Address", "E-mail 1 - Value", "E-Mail", "Email"),
        new("email-2", "E-Mail 2: Adresse", "E-mail 2 Address", "E-mail 2 - Value", "E-Mail 2"),
        new("email-3", "E-Mail 3: Adresse", "E-mail 3 Address", "E-mail 3 - Value", "E-Mail 3"),
        new("phone-mobile", "Mobiltelefon", "Mobile Phone", "Handy", "Mobil"),
        new("phone-work", "Telefon geschäftlich", "Business Phone", "Telefon (geschäftlich)"),
        new("phone-home", "Telefon privat", "Home Phone", "Telefon (privat)"),
        new("phone-main", "Haupttelefon", "Primary Phone", "Main Phone"),
        new("phone-work-fax", "Fax geschäftlich", "Business Fax"),
        new("phone-home-fax", "Fax privat", "Home Fax"),
        new("phone-pager", "Pager"),
        new("phone-other", "Weiteres Telefon", "Other Phone", "Telefon"),
        new("work-street", "Straße geschäftlich", "Business Street"),
        new("work-postal", "Postleitzahl geschäftlich", "Business Postal Code", "PLZ geschäftlich"),
        new("work-city", "Ort geschäftlich", "Business City"),
        new("work-region", "Region geschäftlich", "Business State", "Bundesland/Kanton geschäftlich"),
        new("work-country", "Land/Region geschäftlich", "Business Country/Region", "Business Country"),
        new("home-street", "Straße privat", "Home Street"),
        new("home-postal", "Postleitzahl privat", "Home Postal Code", "PLZ privat"),
        new("home-city", "Ort privat", "Home City"),
        new("home-region", "Region privat", "Home State", "Bundesland/Kanton privat"),
        new("home-country", "Land/Region privat", "Home Country/Region", "Home Country"),
        new("other-street", "Weitere Straße", "Other Street"),
        new("other-postal", "Weitere Postleitzahl", "Other Postal Code", "Postleitzahl weitere"),
        new("other-city", "Weiterer Ort", "Other City", "Ort weitere"),
        new("other-region", "Weitere Region", "Other State", "Region weitere"),
        new("other-country", "Weiteres Land/Region", "Other Country/Region", "Land/Region weitere"),
        new("url", "Webseite", "Web Page", "Website", "Website 1 - Value", "Webadresse"),
        new("birthday", "Geburtstag", "Birthday"),
        new("anniversary", "Jahrestag", "Anniversary"),
        new("notes", "Notizen", "Notes"),
        new("categories", "Kategorien", "Categories", "Group Membership"),
        new("favorite", "Favorit", "Favorite"),
    ];

    /// <summary>The field of an id, or null.</summary>
    public static CsvField? FieldOf(string? id) => Fields.FirstOrDefault(f => f.Id == id);

    // ---- reading ----

    /// <summary>Reads a CSV file. Empty lines are left out; a row with fewer cells is filled with empty ones.</summary>
    public static CsvTable Read(byte[] bytes) => Read(VCard.TextOf(bytes));

    public static CsvTable Read(string text)
    {
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var separator = DetectSeparator(text);
        var records = Records(text, separator).Where(r => r.Any(c => c.Trim().Length > 0)).ToList();
        if (records.Count == 0)
        {
            return new CsvTable([], [], separator);
        }

        var columns = records[0].Select(c => c.Trim()).ToList();
        var rows = records.Skip(1)
            .Select(r => (IReadOnlyList<string>)Enumerable.Range(0, columns.Count).Select(i => i < r.Count ? r[i] : string.Empty).ToList())
            .ToList();
        return new CsvTable(columns, rows, separator);
    }

    /// <summary>The field of every column by its name (case and spaces ignored); a column nobody knows is <see cref="Ignore"/>. A field is given once.</summary>
    public static IReadOnlyList<string> AutoMap(IReadOnlyList<string> columns)
    {
        var used = new HashSet<string>();
        var result = new List<string>();
        foreach (var column in columns)
        {
            var key = HeaderKey(column);
            var field = Fields.FirstOrDefault(f => !used.Contains(f.Id) && (HeaderKey(f.Header) == key || f.Aliases.Any(a => HeaderKey(a) == key)));
            if (field is null)
            {
                result.Add(Ignore);
                continue;
            }

            used.Add(field.Id);
            result.Add(field.Id);
        }

        return result;
    }

    /// <summary>The contacts of the rows, each column into the field <paramref name="mapping"/> names for it; rows without a name, company or e-mail are left out.</summary>
    public static IReadOnlyList<ImportedPerson> Import(CsvTable table, IReadOnlyList<string> mapping)
    {
        var result = new List<ImportedPerson>();
        foreach (var row in table.Rows)
        {
            var values = new Dictionary<string, string>();
            for (var i = 0; i < row.Count && i < mapping.Count; i++)
            {
                var id = mapping[i];
                var cell = row[i].Trim();
                if (id.Length == 0 || cell.Length == 0)
                {
                    continue;
                }

                values[id] = values.TryGetValue(id, out var before) ? before + "\n" + cell : cell;
            }

            var contact = PersonRules.Normalize(ToContact(values));
            if (contact.HasName)
            {
                result.Add(new ImportedPerson(contact));
            }
        }

        return result;
    }

    private static ContactData ToContact(Dictionary<string, string> v)
    {
        string Get(string id) => v.TryGetValue(id, out var value) ? value : string.Empty;
        var emails = new[] { "email-1", "email-2", "email-3" }.Select(Get).Where(e => e.Length > 0).Select(e => new ContactValue(ContactLabels.Other, e)).ToList();
        var phones = new (string Id, string Label)[]
            {
                ("phone-mobile", ContactLabels.Mobile), ("phone-work", ContactLabels.Work), ("phone-home", ContactLabels.Home), ("phone-main", ContactLabels.Main),
                ("phone-work-fax", ContactLabels.WorkFax), ("phone-home-fax", ContactLabels.HomeFax), ("phone-pager", ContactLabels.Pager), ("phone-other", ContactLabels.Other),
            }
            .Where(p => Get(p.Id).Length > 0).Select(p => new ContactValue(p.Label, Get(p.Id))).ToList();
        var addresses = new[] { ContactLabels.Work, ContactLabels.Home, ContactLabels.Other }
            .Select(label => new ContactAddress
            {
                Label = label,
                Street = Get(label + "-street"),
                PostalCode = Get(label + "-postal"),
                City = Get(label + "-city"),
                Region = Get(label + "-region"),
                Country = Get(label + "-country"),
            })
            .Where(a => !a.IsEmpty).ToList();
        var favorite = Get("favorite").ToLowerInvariant();
        return new ContactData
        {
            Prefix = Get("prefix"),
            GivenName = Get("given"),
            MiddleName = Get("middle"),
            FamilyName = Get("family"),
            Suffix = Get("suffix"),
            Nickname = Get("nickname"),
            DisplayName = Get("display"),
            Company = Get("company"),
            Department = Get("department"),
            JobTitle = Get("title"),
            Emails = emails,
            Phones = phones,
            Addresses = addresses,
            Urls = Get("url").Length > 0 ? [new ContactValue(ContactLabels.Work, Get("url"))] : [],
            Birthday = Date(Get("birthday")),
            Anniversary = Date(Get("anniversary")),
            Notes = Get("notes"),
            Categories = Get("categories").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.Trim('*').Trim()).Where(c => c.Length > 0 && !c.Equals("myContacts", StringComparison.OrdinalIgnoreCase)).ToList(),
            IsFavorite = favorite is "ja" or "yes" or "true" or "1" or "wahr" or "x",
        };
    }

    /// <summary>"31.12.1980", "31.12.", "1980-12-31", "12/31/1980", "--12-31", "0.0.00" (Outlook's empty) → "1980-12-31", "--12-31" or empty.</summary>
    internal static string Date(string value)
    {
        var text = value.Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (VCard.Date(text) is { Length: > 0 } iso)
        {
            return iso;
        }

        string[] full = ["d.M.yyyy", "dd.MM.yyyy", "M/d/yyyy", "MM/dd/yyyy", "yyyy/MM/dd"];
        if (DateTime.TryParseExact(text, full, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year > 1800)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var parts = text.TrimEnd('.').Split('.');
        if (parts.Length == 2 && int.TryParse(parts[0], out var day) && int.TryParse(parts[1], out var month))
        {
            var noYear = $"--{month:00}-{day:00}";
            return ContactsContract.IsValidDate(noYear) ? noYear : string.Empty;
        }

        return string.Empty;
    }

    private static char DetectSeparator(string text)
    {
        var end = text.IndexOf('\n');
        var first = end < 0 ? text : text[..end];
        var counts = new Dictionary<char, int> { [';'] = 0, [','] = 0, ['\t'] = 0 };
        var quoted = false;
        foreach (var c in first)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && counts.ContainsKey(c))
            {
                counts[c]++;
            }
        }

        var best = counts.OrderByDescending(p => p.Value).First();
        return best.Value == 0 ? ',' : best.Key;
    }

    private static IEnumerable<List<string>> Records(string text, char separator)
    {
        var record = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(c);
                }

                continue;
            }

            if (c == '"' && cell.Length == 0)
            {
                quoted = true;
            }
            else if (c == separator)
            {
                record.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                record.Add(cell.ToString());
                cell.Clear();
                yield return record;
                record = [];
            }
            else
            {
                cell.Append(c);
            }
        }

        if (cell.Length > 0 || record.Count > 0)
        {
            record.Add(cell.ToString());
            yield return record;
        }
    }

    private static string HeaderKey(string header) => string.Concat(PersonRules.Fold(header).Where(c => char.IsLetterOrDigit(c)));

    // ---- writing ----

    /// <summary>
    /// The contacts as CSV in Outlook's German columns (<see cref="Fields"/>): separator ";", every cell in quotes, CRLF, UTF-8 with BOM (Excel reads the
    /// umlauts), dates as "31.12.1980" (without a year "31.12."), categories with ";" between them.
    /// </summary>
    public static byte[] Write(IEnumerable<ContactData> contacts)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(';', Fields.Select(f => Quote(f.Header)))).Append("\r\n");
        foreach (var contact in contacts)
        {
            sb.Append(string.Join(';', Cells(contact).Select(Quote))).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static IEnumerable<string> Cells(ContactData c)
    {
        string Phone(string label) => c.Phones.FirstOrDefault(p => p.Label == label)?.Value ?? string.Empty;
        ContactAddress? Address(string label) => c.Addresses.FirstOrDefault(a => a.Label == label);
        // e-mail addresses in their order; values of a label CSV has no column for land in "Weiteres Telefon" / "Weitere Straße"
        var otherPhone = c.Phones.FirstOrDefault(p => p.Label == ContactLabels.Other || !ContactLabels.Phone.Contains(p.Label))?.Value ?? string.Empty;
        var otherAddress = Address(ContactLabels.Other) ?? c.Addresses.FirstOrDefault(a => !ContactLabels.Address.Contains(a.Label));
        foreach (var field in Fields)
        {
            yield return field.Id switch
            {
                "prefix" => c.Prefix,
                "given" => c.GivenName,
                "middle" => c.MiddleName,
                "family" => c.FamilyName,
                "suffix" => c.Suffix,
                "nickname" => c.Nickname,
                "display" => c.DisplayName,
                "company" => c.Company,
                "department" => c.Department,
                "title" => c.JobTitle,
                "email-1" => c.Emails.ElementAtOrDefault(0)?.Value ?? string.Empty,
                "email-2" => c.Emails.ElementAtOrDefault(1)?.Value ?? string.Empty,
                "email-3" => c.Emails.ElementAtOrDefault(2)?.Value ?? string.Empty,
                "phone-mobile" => Phone(ContactLabels.Mobile),
                "phone-work" => Phone(ContactLabels.Work),
                "phone-home" => Phone(ContactLabels.Home),
                "phone-main" => Phone(ContactLabels.Main),
                "phone-work-fax" => Phone(ContactLabels.WorkFax),
                "phone-home-fax" => Phone(ContactLabels.HomeFax),
                "phone-pager" => Phone(ContactLabels.Pager),
                "phone-other" => otherPhone,
                "url" => c.Urls.FirstOrDefault()?.Value ?? string.Empty,
                "birthday" => CsvDate(c.Birthday),
                "anniversary" => CsvDate(c.Anniversary),
                "notes" => c.Notes,
                "categories" => string.Join(";", c.Categories),
                "favorite" => c.IsFavorite ? "Ja" : string.Empty,
                var id when id.EndsWith("-street", StringComparison.Ordinal) => AddressPart(id, a => a.Street),
                var id when id.EndsWith("-postal", StringComparison.Ordinal) => AddressPart(id, a => a.PostalCode),
                var id when id.EndsWith("-city", StringComparison.Ordinal) => AddressPart(id, a => a.City),
                var id when id.EndsWith("-region", StringComparison.Ordinal) => AddressPart(id, a => a.Region),
                var id when id.EndsWith("-country", StringComparison.Ordinal) => AddressPart(id, a => a.Country),
                _ => string.Empty,
            };
        }

        string AddressPart(string id, Func<ContactAddress, string> part)
        {
            var label = id[..id.IndexOf('-')];
            var address = label == ContactLabels.Other ? otherAddress : Address(label);
            return address is null ? string.Empty : part(address);
        }
    }

    private static string CsvDate(string date)
    {
        if (!ContactsContract.TryParseDate(date, out var year, out var month, out var day))
        {
            return string.Empty;
        }

        return year is null ? $"{day:00}.{month:00}." : $"{day:00}.{month:00}.{year:0000}";
    }

    private static string Quote(string cell) => "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
