using ThixCalc.Core.Model;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Editing;

/// <summary>
/// Überprüfen › Blatt schützen / Arbeitsmappe schützen (with or without a password). The session enforces them – the business
/// layer, not the UI: locked cells of a protected sheet keep their content, formats change only when allowed, rows and columns are
/// inserted, deleted, sized and hidden only when allowed, and a protected workbook structure keeps its sheets.
/// </summary>
public sealed partial class EditingSession
{
    private const string ProtectedMessage =
        "Die Zelle oder das Diagramm, die bzw. das Sie ändern möchten, befindet sich auf einem geschützten Blatt. Um eine Änderung vorzunehmen, heben Sie den Blattschutz auf (Überprüfen › Blattschutz aufheben). Sie werden ggf. aufgefordert, ein Kennwort einzugeben.";

    private const string StructureMessage = "Die Arbeitsmappe ist geschützt: Blätter können nicht eingefügt, gelöscht, umbenannt, verschoben oder ausgeblendet werden.";

    /// <summary>Whether the active sheet is protected.</summary>
    public bool IsSheetProtected => ActiveSheet.Protection is not null;

    /// <summary>Blatt schützen with what stays allowed and an optional password.</summary>
    public void ProtectSheet(SheetProtection allowed, string? password)
    {
        var sheet = ActiveSheet;
        var xml = Protection.SheetXml(allowed with { HasPassword = !string.IsNullOrEmpty(password) }, password);
        SetProtection(sheet, xml, "Blatt schützen");
    }

    /// <summary>Blattschutz aufheben: false (nothing changes) when the password does not match.</summary>
    public bool UnprotectSheet(string? password)
    {
        var sheet = ActiveSheet;
        if (sheet.ProtectionXml is not { } xml || sheet.Protection is null)
        {
            return true;
        }

        if (!Protection.Verify(xml, password))
        {
            throw new BusinessException("Das eingegebene Kennwort ist falsch. Achten Sie auf Groß- und Kleinschreibung.", "password");
        }

        SetProtection(sheet, null, "Blattschutz aufheben");
        return true;
    }

    /// <summary>Arbeitsmappe schützen (Struktur) with an optional password.</summary>
    public void ProtectWorkbook(string? password)
    {
        var before = Workbook.ProtectionXml;
        var after = Protection.WorkbookXml(password);
        Execute("Arbeitsmappe schützen", () =>
        {
            Workbook.ProtectionXml = after;
            Record(new ActionStep(() => Workbook.ProtectionXml = before, () => Workbook.ProtectionXml = after));
        });
    }

    public void UnprotectWorkbook(string? password)
    {
        if (Workbook.ProtectionXml is not { } xml)
        {
            return;
        }

        if (!Protection.Verify(xml, password, workbook: true))
        {
            throw new BusinessException("Das eingegebene Kennwort ist falsch. Achten Sie auf Groß- und Kleinschreibung.", "password");
        }

        Execute("Arbeitsmappenschutz aufheben", () =>
        {
            Workbook.ProtectionXml = null;
            Record(new ActionStep(() => Workbook.ProtectionXml = xml, () => Workbook.ProtectionXml = null));
        });
    }

    private void SetProtection(Worksheet sheet, string? xml, string label)
    {
        var before = sheet.ProtectionXml;
        Execute(label, () =>
        {
            sheet.ProtectionXml = xml;
            Record(new ActionStep(() => sheet.ProtectionXml = before, () => sheet.ProtectionXml = xml));
        });
    }

    /// <summary>A value or format change of a cell on a protected sheet (inside the open command).</summary>
    private void GuardProtection(Worksheet sheet, int row, int column, Cell before, Cell after)
    {
        if (sheet.Protection is not { } protection || _writingPivot)
        {
            return;
        }

        var contentChanges = !before.Value.Equals(after.Value) || before.Formula != after.Formula;
        if (contentChanges && Workbook.Styles[sheet.GetStyleId(row, column)].Protection.Locked)
        {
            throw new BusinessException(ProtectedMessage);
        }

        if (before.StyleId != after.StyleId && !protection.FormatCells)
        {
            throw new BusinessException(ProtectedMessage);
        }
    }

    /// <summary>Row and column changes on a protected sheet (sizes, hiding, inserting, deleting) when not allowed.</summary>
    private void GuardAxes(Worksheet sheet, bool rows, bool structural, bool deleting = false)
    {
        if (sheet.Protection is not { } protection)
        {
            return;
        }

        var allowed = structural
            ? (rows ? (deleting ? protection.DeleteRows : protection.InsertRows) : (deleting ? protection.DeleteColumns : protection.InsertColumns))
            : (rows ? protection.FormatRows : protection.FormatColumns);
        if (!allowed)
        {
            throw new BusinessException(ProtectedMessage);
        }
    }

    /// <summary>Sheets added, deleted, renamed, moved or hidden while the structure is protected.</summary>
    private void GuardStructure()
    {
        if (Workbook.IsStructureProtected)
        {
            throw new BusinessException(StructureMessage);
        }
    }
}
