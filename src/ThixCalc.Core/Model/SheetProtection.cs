using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace ThixCalc.Core.Model;

/// <summary>
/// Blatt schützen: what users may still do on a protected sheet (Excel's check boxes; locked cells are never changed). The file keeps
/// it as &lt;sheetProtection&gt;, the password as Excel's SHA-512 hash with salt and spin count (older files: the 16-bit hash).
/// </summary>
public sealed record SheetProtection
{
    public bool SelectLockedCells { get; init; } = true;

    public bool SelectUnlockedCells { get; init; } = true;

    public bool FormatCells { get; init; }

    public bool FormatColumns { get; init; }

    public bool FormatRows { get; init; }

    public bool InsertColumns { get; init; }

    public bool InsertRows { get; init; }

    public bool InsertHyperlinks { get; init; }

    public bool DeleteColumns { get; init; }

    public bool DeleteRows { get; init; }

    public bool Sort { get; init; }

    public bool AutoFilter { get; init; }

    public bool PivotTables { get; init; }

    public bool HasPassword { get; init; }
}

/// <summary>Reading, writing and checking &lt;sheetProtection&gt; / &lt;workbookProtection&gt; (business logic, no UI).</summary>
public static class Protection
{
    private const int SpinCount = 100_000;

    /// <summary>The protection of a sheet's XML, or null when the sheet is not protected.</summary>
    public static SheetProtection? Parse(string? xml)
    {
        if (string.IsNullOrEmpty(xml))
        {
            return null;
        }

        var element = XElement.Parse(xml);
        if (!Flag(element, "sheet", false))
        {
            return null;
        }

        // In the file an attribute says the action is protected (true) – the model says what is allowed.
        return new SheetProtection
        {
            SelectLockedCells = !Flag(element, "selectLockedCells", false),
            SelectUnlockedCells = !Flag(element, "selectUnlockedCells", false),
            FormatCells = !Flag(element, "formatCells", true),
            FormatColumns = !Flag(element, "formatColumns", true),
            FormatRows = !Flag(element, "formatRows", true),
            InsertColumns = !Flag(element, "insertColumns", true),
            InsertRows = !Flag(element, "insertRows", true),
            InsertHyperlinks = !Flag(element, "insertHyperlinks", true),
            DeleteColumns = !Flag(element, "deleteColumns", true),
            DeleteRows = !Flag(element, "deleteRows", true),
            Sort = !Flag(element, "sort", true),
            AutoFilter = !Flag(element, "autoFilter", true),
            PivotTables = !Flag(element, "pivotTables", true),
            HasPassword = element.Attribute("password") is not null || element.Attribute("hashValue") is not null,
        };
    }

    /// <summary>&lt;sheetProtection&gt; for a protection and an optional password (SHA-512, a fresh salt).</summary>
    public static string SheetXml(SheetProtection protection, string? password)
    {
        var element = new XElement(XName.Get("sheetProtection", Formats.Xlsx.XlsxNamespaces.Main));
        Password(element, password);
        element.SetAttributeValue("sheet", "1");
        element.SetAttributeValue("objects", "1");
        element.SetAttributeValue("scenarios", "1");
        void Allowed(string name, bool allowed, bool fileDefault)
        {
            var value = !allowed;
            if (value != fileDefault)
            {
                element.SetAttributeValue(name, value ? "1" : "0");
            }
        }

        Allowed("formatCells", protection.FormatCells, true);
        Allowed("formatColumns", protection.FormatColumns, true);
        Allowed("formatRows", protection.FormatRows, true);
        Allowed("insertColumns", protection.InsertColumns, true);
        Allowed("insertRows", protection.InsertRows, true);
        Allowed("insertHyperlinks", protection.InsertHyperlinks, true);
        Allowed("deleteColumns", protection.DeleteColumns, true);
        Allowed("deleteRows", protection.DeleteRows, true);
        Allowed("selectLockedCells", protection.SelectLockedCells, false);
        Allowed("sort", protection.Sort, true);
        Allowed("autoFilter", protection.AutoFilter, true);
        Allowed("pivotTables", protection.PivotTables, true);
        Allowed("selectUnlockedCells", protection.SelectUnlockedCells, false);
        return element.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>&lt;workbookProtection&gt; locking the structure (sheets cannot be added, deleted, renamed, moved, hidden).</summary>
    public static string WorkbookXml(string? password)
    {
        var element = new XElement(XName.Get("workbookProtection", Formats.Xlsx.XlsxNamespaces.Main));
        if (!string.IsNullOrEmpty(password))
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            element.SetAttributeValue("workbookAlgorithmName", "SHA-512");
            element.SetAttributeValue("workbookHashValue", Convert.ToBase64String(Hash(password, salt, SpinCount)));
            element.SetAttributeValue("workbookSaltValue", Convert.ToBase64String(salt));
            element.SetAttributeValue("workbookSpinCount", SpinCount.ToString(CultureInfo.InvariantCulture));
        }

        element.SetAttributeValue("lockStructure", "1");
        return element.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Whether the workbook XML locks the structure.</summary>
    public static bool LocksStructure(string? xml) => !string.IsNullOrEmpty(xml) && Flag(XElement.Parse(xml), "lockStructure", false);

    /// <summary>Whether <paramref name="password"/> opens the protection (no password set: any, also none).</summary>
    public static bool Verify(string xml, string? password, bool workbook = false)
    {
        var element = XElement.Parse(xml);
        var prefix = workbook ? "workbook" : string.Empty;
        string Name(string name) => workbook ? prefix + char.ToUpperInvariant(name[0]) + name[1..] : name;
        if (element.Attribute(Name("hashValue"))?.Value is { } hash)
        {
            if (element.Attribute(Name("algorithmName"))?.Value is not ("SHA-512" or null))
            {
                return false; // an algorithm ThixCalc does not compute: only Excel can unprotect
            }

            var salt = Convert.FromBase64String(element.Attribute(Name("saltValue"))?.Value ?? string.Empty);
            var spin = int.TryParse(element.Attribute(Name("spinCount"))?.Value, out var count) ? count : 0;
            return CryptographicOperations.FixedTimeEquals(Hash(password ?? string.Empty, salt, spin), Convert.FromBase64String(hash));
        }

        if (element.Attribute(workbook ? "workbookPassword" : "password")?.Value is { } legacy)
        {
            return string.Equals(LegacyHash(password ?? string.Empty), legacy, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    /// <summary>Excel's 16-bit password hash (the "password" attribute of older files), as hex.</summary>
    public static string LegacyHash(string password)
    {
        var hash = 0;
        var bytes = Encoding.ASCII.GetBytes(password);
        for (var index = bytes.Length - 1; index >= 0; index--)
        {
            hash = ((hash >> 14) & 0x01) | ((hash << 1) & 0x7FFF);
            hash ^= bytes[index];
        }

        hash = ((hash >> 14) & 0x01) | ((hash << 1) & 0x7FFF);
        hash ^= bytes.Length;
        hash ^= 0xCE4B;
        return hash.ToString("X4", CultureInfo.InvariantCulture);
    }

    /// <summary>ECMA-376 agile password hash: SHA-512(salt + UTF-16LE password), then spin times SHA-512(hash + iteration).</summary>
    public static byte[] Hash(string password, byte[] salt, int spinCount)
    {
        var hash = SHA512.HashData([.. salt, .. Encoding.Unicode.GetBytes(password)]);
        var buffer = new byte[hash.Length + 4];
        for (var iteration = 0; iteration < spinCount; iteration++)
        {
            hash.CopyTo(buffer, 0);
            BitConverter.TryWriteBytes(buffer.AsSpan(hash.Length), iteration);
            hash = SHA512.HashData(buffer);
        }

        return hash;
    }

    private static void Password(XElement element, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        element.SetAttributeValue("algorithmName", "SHA-512");
        element.SetAttributeValue("hashValue", Convert.ToBase64String(Hash(password, salt, SpinCount)));
        element.SetAttributeValue("saltValue", Convert.ToBase64String(salt));
        element.SetAttributeValue("spinCount", SpinCount.ToString(CultureInfo.InvariantCulture));
    }

    private static bool Flag(XElement element, string name, bool fallback) =>
        element.Attribute(name)?.Value switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => fallback,
        };
}
