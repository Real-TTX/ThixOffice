using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MatCloud.Contract.Contacts;

namespace ThixOffice.Pim.Rules;

/// <summary>
/// A contact read from a file: the data (its <see cref="ContactData.PhotoId"/> is the SHA-256 of <see cref="Photo"/>, still to be kept), the bytes of an
/// embedded picture and the UID the file gave it.
/// </summary>
public sealed record ImportedPerson(ContactData Data, byte[]? Photo = null, string? PhotoType = null, string? Uid = null);

/// <summary>A contact to write into a file: its key (the UID), its data and the bytes of its picture (or null).</summary>
public sealed record ExportedPerson(string Key, ContactData Data, byte[]? Photo = null);

/// <summary>
/// vCard 2.1, 3.0 and 4.0 (RFC 2426, RFC 6350) read, 3.0 and 4.0 written – own code, no library. The same rules as MatCloud's module Contacts (<c>VCard</c> there)
/// (the fixtures in tests/ThixOffice.Pim.Tests/Fixtures/Contacts – the same files as MatCloud's – hold both to the same results):
/// - read: folded lines, groups ("item1.EMAIL"), parameters with and without "TYPE=", quoted-printable and the charsets of 2.1, escapes, N/ADR/ORG parts,
///   pictures inline (base64 of 2.1/3.0, a data: address of 4.0; a picture behind a web address is not fetched), BDAY/ANNIVERSARY with or without year
///   (also X-ANNIVERSARY, X-MS-ANNIVERSARY), CATEGORIES, X-THIX-FAVORITE; unknown properties are left out.
/// - labels: work/home; cell → mobile, fax with work/home → work-fax/home-fax (fax alone: work-fax), pager, main; anything else → other.
/// - FN is the display name only when it differs from the name built from N.
/// - write: CRLF, lines folded at 75 bytes (never inside a UTF-8 character), UID = the key, FN always, N always.
/// </summary>
public static class VCard
{
    /// <summary>The contacts of a file (any number of BEGIN:VCARD … END:VCARD); a card without a name, a company or an e-mail is left out.</summary>
    public static IReadOnlyList<ImportedPerson> Read(string text)
    {
        var result = new List<ImportedPerson>();
        List<Property>? card = null;
        foreach (var property in Properties(text))
        {
            if (property.Name == "BEGIN" && property.Value.Equals("VCARD", StringComparison.OrdinalIgnoreCase))
            {
                card = [];
                continue;
            }

            if (property.Name == "END" && property.Value.Equals("VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (card is not null && ToContact(card) is { } contact)
                {
                    result.Add(contact);
                }

                card = null;
                continue;
            }

            card?.Add(property);
        }

        return result;
    }

    /// <summary>Reads the bytes of a file: UTF-8 (with or without BOM), UTF-16 with BOM, else Latin-1.</summary>
    public static IReadOnlyList<ImportedPerson> Read(byte[] bytes) => Read(TextOf(bytes));

    /// <summary>The text of a file's bytes: UTF-8 when it is valid UTF-8, UTF-16 with its BOM, else Latin-1.</summary>
    public static string TextOf(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>The cards of <paramref name="contacts"/> in one text (version 3.0 or 4.0).</summary>
    public static string Write(IEnumerable<ExportedPerson> contacts, int version = 3)
    {
        var sb = new StringBuilder();
        foreach (var contact in contacts)
        {
            WriteCard(sb, contact, version == 4 ? 4 : 3);
        }

        return sb.ToString();
    }

    // ---- reading ----

    private sealed record Property(string Group, string Name, IReadOnlyList<(string Name, string Value)> Parameters, string Value)
    {
        public IEnumerable<string> Types => Parameters
            .Where(p => p.Name is "TYPE" or "")
            .SelectMany(p => p.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(t => t.Trim('"').ToLowerInvariant());

        public string? Parameter(string name) => Parameters.FirstOrDefault(p => p.Name == name).Value;
    }

    private static IEnumerable<Property> Properties(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var current = new StringBuilder();
        var quotedPrintable = false;
        foreach (var raw in lines)
        {
            if (current.Length > 0 && raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && !quotedPrintable)
            {
                current.Append(raw, 1, raw.Length - 1);
                continue;
            }

            if (current.Length > 0 && quotedPrintable && current[^1] == '=')
            {
                // a soft line break of quoted-printable (vCard 2.1): the next line goes on
                current.Length--;
                current.Append(raw);
                continue;
            }

            if (current.Length > 0 && Parse(current.ToString()) is { } done)
            {
                yield return done;
            }

            current.Clear();
            current.Append(raw);
            var colon = raw.IndexOf(':');
            quotedPrintable = colon > 0 && raw[..colon].Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);
        }

        if (current.Length > 0 && Parse(current.ToString()) is { } last)
        {
            yield return last;
        }
    }

    private static Property? Parse(string line)
    {
        // the name and the parameters end at the first colon outside quotes
        var inQuotes = false;
        var colon = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (line[i] == ':' && !inQuotes)
            {
                colon = i;
                break;
            }
        }

        if (colon <= 0)
        {
            return null;
        }

        var head = SplitOutsideQuotes(line[..colon], ';');
        var name = head[0].Trim();
        var group = string.Empty;
        var dot = name.LastIndexOf('.');
        if (dot >= 0)
        {
            group = name[..dot];
            name = name[(dot + 1)..];
        }

        var parameters = new List<(string, string)>();
        foreach (var part in head.Skip(1))
        {
            var equals = part.IndexOf('=');
            if (equals < 0)
            {
                // vCard 2.1: a bare type ("TEL;WORK;VOICE:") or encoding ("QUOTED-PRINTABLE", "BASE64")
                var bare = part.Trim().ToUpperInvariant();
                parameters.Add(bare is "QUOTED-PRINTABLE" or "BASE64" or "B" ? ("ENCODING", bare) : (string.Empty, part.Trim()));
            }
            else
            {
                parameters.Add((part[..equals].Trim().ToUpperInvariant(), part[(equals + 1)..].Trim()));
            }
        }

        var value = line[(colon + 1)..];
        var encoding = parameters.FirstOrDefault(p => p.Item1 == "ENCODING").Item2?.ToUpperInvariant();
        if (encoding == "QUOTED-PRINTABLE")
        {
            value = DecodeQuotedPrintable(value, parameters.FirstOrDefault(p => p.Item1 == "CHARSET").Item2);
        }

        return new Property(group, name.ToUpperInvariant(), parameters, value);
    }

    private static ImportedPerson? ToContact(List<Property> card)
    {
        var contact = new ContactData();
        string? fn = null;
        byte[]? photo = null;
        string? photoType = null;
        string? uid = null;
        var emails = new List<ContactValue>();
        var phones = new List<ContactValue>();
        var urls = new List<ContactValue>();
        var addresses = new List<ContactAddress>();
        var categories = new List<string>();
        string? role = null;

        // Apple writes the label of a value as a property of the same group: item1.X-ABLabel:_$!<Mobile>!$_
        var groupLabels = card.Where(p => p.Name == "X-ABLABEL" && p.Group.Length > 0)
            .GroupBy(p => p.Group).ToDictionary(g => g.Key, g => AppleLabel(Unescape(g.First().Value)));

        foreach (var p in card)
        {
            var value = p.Value;
            switch (p.Name)
            {
                case "FN":
                    fn = Unescape(value).Trim();
                    break;
                case "N":
                    var n = Components(value);
                    contact = contact with
                    {
                        FamilyName = Part(n, 0),
                        GivenName = Part(n, 1),
                        MiddleName = Part(n, 2),
                        Prefix = Part(n, 3),
                        Suffix = Part(n, 4),
                    };
                    break;
                case "NICKNAME":
                    contact = contact with { Nickname = string.Join(", ", SplitList(value)) };
                    break;
                case "ORG":
                    var org = Components(value);
                    contact = contact with { Company = Part(org, 0), Department = string.Join(", ", org.Skip(1).Select(o => o.Trim()).Where(o => o.Length > 0)) };
                    break;
                case "TITLE":
                    contact = contact with { JobTitle = Unescape(value).Trim() };
                    break;
                case "ROLE":
                    role = Unescape(value).Trim();
                    break;
                case "EMAIL":
                    emails.Add(new ContactValue(LabelOf(p, groupLabels, false), Unescape(value).Trim()));
                    break;
                case "TEL":
                    phones.Add(new ContactValue(LabelOf(p, groupLabels, true), StripTel(Unescape(value).Trim())));
                    break;
                case "URL":
                    urls.Add(new ContactValue(LabelOf(p, groupLabels, false), Unescape(value).Trim()));
                    break;
                case "ADR":
                    var adr = Components(value);
                    var street = string.Join('\n', new[] { Part(adr, 0), Part(adr, 1), Part(adr, 2) }.Where(s => s.Length > 0));
                    addresses.Add(new ContactAddress
                    {
                        Label = LabelOf(p, groupLabels, false),
                        Street = street,
                        City = Part(adr, 3),
                        Region = Part(adr, 4),
                        PostalCode = Part(adr, 5),
                        Country = Part(adr, 6),
                    });
                    break;
                case "BDAY":
                    contact = contact with { Birthday = Date(Unescape(value)) };
                    break;
                case "ANNIVERSARY" or "X-ANNIVERSARY" or "X-MS-ANNIVERSARY" or "X-EVOLUTION-ANNIVERSARY":
                    if (contact.Anniversary.Length == 0)
                    {
                        contact = contact with { Anniversary = Date(Unescape(value)) };
                    }

                    break;
                case "NOTE":
                    contact = contact with { Notes = contact.Notes.Length == 0 ? Unescape(value) : contact.Notes + "\n" + Unescape(value) };
                    break;
                case "CATEGORIES":
                    categories.AddRange(SplitList(value));
                    break;
                case "X-THIX-FAVORITE":
                    contact = contact with { IsFavorite = value.Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase) || value.Trim() == "1" };
                    break;
                case "UID":
                    uid = Unescape(value).Trim();
                    break;
                case "PHOTO" when photo is null:
                    (photo, photoType) = Picture(p);
                    break;
            }
        }

        contact = PersonRules.Normalize(contact with
        {
            JobTitle = contact.JobTitle.Length > 0 ? contact.JobTitle : role ?? string.Empty,
            Emails = emails,
            Phones = phones,
            Urls = urls,
            Addresses = addresses,
            Categories = categories,
            PhotoId = photo is null ? string.Empty : Convert.ToHexStringLower(SHA256.HashData(photo)),
        });

        // FN is the display name only when it is not the name the parts give anyway (that is what a writer puts there)
        var withAffixes = string.Join(' ', new[] { contact.Prefix, contact.GivenName, contact.MiddleName, contact.FamilyName, contact.Suffix }.Where(s => s.Length > 0));
        if (!string.IsNullOrEmpty(fn) && fn != contact.NameToShow() && fn != withAffixes)
        {
            contact = contact with { DisplayName = fn };
        }

        return contact.HasName ? new ImportedPerson(contact, photo, photoType, uid) : null;
    }

    private static string LabelOf(Property p, Dictionary<string, string> groupLabels, bool phone)
    {
        if (p.Group.Length > 0 && groupLabels.TryGetValue(p.Group, out var apple) && apple.Length > 0)
        {
            return apple;
        }

        var types = p.Types.ToHashSet();
        if (phone)
        {
            if (types.Contains("fax"))
            {
                return types.Contains("home") ? ContactLabels.HomeFax : ContactLabels.WorkFax;
            }

            if (types.Contains("cell") || types.Contains("mobile") || types.Contains("iphone"))
            {
                return ContactLabels.Mobile;
            }

            if (types.Contains("pager"))
            {
                return ContactLabels.Pager;
            }

            if (types.Contains("main"))
            {
                return ContactLabels.Main;
            }
        }

        if (types.Contains("work"))
        {
            return ContactLabels.Work;
        }

        return types.Contains("home") ? ContactLabels.Home : ContactLabels.Other;
    }

    /// <summary>Apple's labels: "_$!&lt;Mobile&gt;!$_" and its relatives; a label of the user's own stays as written (small letters).</summary>
    private static string AppleLabel(string label)
    {
        var text = label.Trim();
        if (text.StartsWith("_$!<", StringComparison.Ordinal) && text.EndsWith(">!$_", StringComparison.Ordinal))
        {
            text = text[4..^4];
        }

        return text.ToLowerInvariant() switch
        {
            "mobile" or "iphone" => ContactLabels.Mobile,
            "work" => ContactLabels.Work,
            "home" => ContactLabels.Home,
            "main" => ContactLabels.Main,
            "workfax" => ContactLabels.WorkFax,
            "homefax" => ContactLabels.HomeFax,
            "pager" => ContactLabels.Pager,
            "homepage" => ContactLabels.Profile,
            "other" => ContactLabels.Other,
            var own => own,
        };
    }

    /// <summary>"tel:+49 89 1234" (4.0 VALUE=uri) → "+49 89 1234".</summary>
    private static string StripTel(string value) => value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ? value[4..] : value;

    private static (byte[]? Bytes, string? Type) Picture(Property p)
    {
        var value = p.Value.Trim();
        try
        {
            if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = value.IndexOf(',');
                var head = comma > 0 ? value[5..comma] : string.Empty;
                if (comma < 0 || !head.Contains(";base64", StringComparison.OrdinalIgnoreCase))
                {
                    return (null, null);
                }

                return (Convert.FromBase64String(value[(comma + 1)..]), head.Split(';')[0].ToLowerInvariant());
            }

            var encoding = p.Parameter("ENCODING")?.ToUpperInvariant();
            if (encoding is "B" or "BASE64")
            {
                var type = p.Types.FirstOrDefault() ?? p.Parameter("TYPE")?.ToLowerInvariant();
                return (Convert.FromBase64String(string.Concat(value.Where(c => !char.IsWhiteSpace(c)))), type is null ? null : "image/" + type.Replace("image/", string.Empty));
            }
        }
        catch (FormatException)
        {
            // a broken picture is left out, the contact is not
        }

        return (null, null);
    }

    /// <summary>"1980-05-01", "19800501", "--0501", "--05-01", "1980-05-01T00:00:00Z" → "1980-05-01" or "--05-01"; anything else → empty.</summary>
    internal static string Date(string value)
    {
        var text = value.Trim();
        var t = text.IndexOf('T');
        if (t > 0)
        {
            text = text[..t];
        }

        string candidate;
        if (text.StartsWith("--", StringComparison.Ordinal))
        {
            var rest = text[2..].Replace("-", string.Empty, StringComparison.Ordinal);
            candidate = rest.Length == 4 ? $"--{rest[..2]}-{rest[2..]}" : string.Empty;
        }
        else
        {
            var digits = text.Replace("-", string.Empty, StringComparison.Ordinal);
            candidate = digits.Length == 8 && digits.All(char.IsAsciiDigit) ? $"{digits[..4]}-{digits[4..6]}-{digits[6..]}" : string.Empty;
        }

        return ContactsContract.IsValidDate(candidate) ? candidate : string.Empty;
    }

    private static List<string> Components(string value) => SplitEscaped(value, ';').Select(c => string.Join(' ', SplitEscaped(c, ',').Select(Unescape).Where(s => s.Trim().Length > 0).Select(s => s.Trim()))).ToList();

    private static string Part(List<string> parts, int index) => index < parts.Count ? parts[index].Trim() : string.Empty;

    private static IEnumerable<string> SplitList(string value) => SplitEscaped(value, ',').Select(Unescape).Select(s => s.Trim()).Where(s => s.Length > 0);

    /// <summary>Splits at a separator that is not escaped with a backslash.</summary>
    private static List<string> SplitEscaped(string value, char separator)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                current.Append(c).Append(value[++i]);
            }
            else if (c == separator)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        parts.Add(current.ToString());
        return parts;
    }

    private static List<string> SplitOutsideQuotes(string value, char separator)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var c in value)
        {
            if (c == '"')
            {
                quoted = !quoted;
                current.Append(c);
            }
            else if (c == separator && !quoted)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        parts.Add(current.ToString());
        return parts;
    }

    private static string Unescape(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string DecodeQuotedPrintable(string value, string? charset)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                bytes.Add(byte.Parse(value.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 2;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value[i].ToString()));
            }
        }

        var encoding = charset?.Trim('"').ToUpperInvariant() switch
        {
            "ISO-8859-1" or "LATIN1" or "WINDOWS-1252" => Encoding.Latin1,
            _ => Encoding.UTF8,
        };
        return encoding.GetString(bytes.ToArray());
    }

    // ---- writing ----

    private static void WriteCard(StringBuilder sb, ExportedPerson exported, int version)
    {
        var c = exported.Data;
        Line(sb, "BEGIN:VCARD");
        Line(sb, version == 4 ? "VERSION:4.0" : "VERSION:3.0");
        Line(sb, "UID:" + Escape(exported.Key));
        Line(sb, "FN:" + Escape(c.NameToShow()));
        Line(sb, "N:" + string.Join(';', new[] { c.FamilyName, c.GivenName, c.MiddleName, c.Prefix, c.Suffix }.Select(EscapePart)));
        if (c.Nickname.Length > 0)
        {
            Line(sb, "NICKNAME:" + Escape(c.Nickname));
        }

        if (c.Company.Length > 0 || c.Department.Length > 0)
        {
            Line(sb, "ORG:" + EscapePart(c.Company) + (c.Department.Length > 0 ? ";" + EscapePart(c.Department) : string.Empty));
        }

        if (c.JobTitle.Length > 0)
        {
            Line(sb, "TITLE:" + Escape(c.JobTitle));
        }

        // a label of the user's own goes as Apple writes it: the property in a group with an X-ABLabel of the same group
        var group = 0;
        string Own(string label, IReadOnlyList<string> known, string line)
        {
            if (known.Contains(label))
            {
                return line;
            }

            group++;
            Line(sb, $"item{group}.X-ABLabel:" + Escape(label));
            return $"item{group}." + line;
        }

        foreach (var e in c.Emails)
        {
            Line(sb, Own(e.Label, ContactLabels.Email, "EMAIL" + TypeParameter(version, version == 3 ? ["internet", .. ValueTypes(e.Label)] : ValueTypes(e.Label)) + ":" + Escape(e.Value)));
        }

        foreach (var p in c.Phones)
        {
            Line(sb, Own(p.Label, ContactLabels.Phone, "TEL" + TypeParameter(version, PhoneTypes(p.Label)) + ":" + Escape(p.Value)));
        }

        foreach (var a in c.Addresses)
        {
            Line(sb, Own(a.Label, ContactLabels.Address, "ADR" + TypeParameter(version, ValueTypes(a.Label)) + ":" +
                     string.Join(';', new[] { string.Empty, string.Empty, a.Street, a.City, a.Region, a.PostalCode, a.Country }.Select(EscapePart))));
        }

        foreach (var u in c.Urls)
        {
            Line(sb, Own(u.Label, ContactLabels.Url, "URL" + TypeParameter(version, ValueTypes(u.Label)) + ":" + u.Value));
        }

        if (c.Birthday.Length > 0)
        {
            Line(sb, "BDAY:" + c.Birthday);
        }

        if (c.Anniversary.Length > 0)
        {
            Line(sb, (version == 4 ? "ANNIVERSARY:" : "X-ANNIVERSARY:") + c.Anniversary);
        }

        if (c.Notes.Length > 0)
        {
            Line(sb, "NOTE:" + Escape(c.Notes));
        }

        if (c.Categories.Count > 0)
        {
            Line(sb, "CATEGORIES:" + string.Join(',', c.Categories.Select(EscapePart)));
        }

        if (c.IsFavorite)
        {
            Line(sb, "X-THIX-FAVORITE:TRUE");
        }

        if (exported.Photo is { Length: > 0 } photo && ImageTypeOf(photo) is { } type)
        {
            Line(sb, version == 4
                ? $"PHOTO:data:{type};base64,{Convert.ToBase64String(photo)}"
                : $"PHOTO;ENCODING=b;TYPE={type["image/".Length..].ToUpperInvariant()}:{Convert.ToBase64String(photo)}");
        }

        Line(sb, "END:VCARD");
    }

    /// <summary>The MIME type of a picture by its first bytes (JPEG, PNG, GIF, WebP), or null.</summary>
    public static string? ImageTypeOf(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return "image/png";
        }

        if (bytes.Length >= 6 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8')
        {
            return "image/gif";
        }

        return bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P'
            ? "image/webp"
            : null;
    }

    private static string[] ValueTypes(string label) => label switch
    {
        ContactLabels.Work => ["work"],
        ContactLabels.Home => ["home"],
        _ => [],
    };

    private static string[] PhoneTypes(string label) => label switch
    {
        ContactLabels.Mobile => ["cell"],
        ContactLabels.Work => ["work", "voice"],
        ContactLabels.Home => ["home", "voice"],
        ContactLabels.Main => ["main"],
        ContactLabels.WorkFax => ["work", "fax"],
        ContactLabels.HomeFax => ["home", "fax"],
        ContactLabels.Pager => ["pager"],
        _ => ["voice"],
    };

    /// <summary>";TYPE=WORK,VOICE" (3.0, capitals) or ";TYPE=work,voice" (4.0); nothing without types.</summary>
    private static string TypeParameter(int version, string[] types)
        => types.Length == 0 ? string.Empty : ";TYPE=" + string.Join(',', version == 3 ? types.Select(t => t.ToUpperInvariant()) : types);

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static string EscapePart(string value) => Escape(value);

    /// <summary>A content line, folded after 75 bytes of UTF-8 (a continuation starts with a space), ended with CRLF.</summary>
    private static void Line(StringBuilder sb, string line)
    {
        var bytes = 0;
        var limit = 75;
        for (var i = 0; i < line.Length; i++)
        {
            var length = char.IsHighSurrogate(line[i]) && i + 1 < line.Length ? 2 : 1;
            var size = Encoding.UTF8.GetByteCount(line.AsSpan(i, length));
            if (bytes + size > limit)
            {
                sb.Append("\r\n ");
                bytes = 1;
                limit = 75;
            }

            sb.Append(line, i, length);
            bytes += size;
            i += length - 1;
        }

        sb.Append("\r\n");
    }
}
