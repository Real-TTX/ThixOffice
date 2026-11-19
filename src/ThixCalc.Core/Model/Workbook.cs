using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Model;

/// <summary>A changed region of a sheet (the grid redraws it).</summary>
public readonly record struct SheetRegion(Worksheet Sheet, CellRange Range, bool StyleOnly = false);

/// <summary>What changed since the last <see cref="Workbook.Changed"/>: regions, and whether rows/columns/sheets changed.</summary>
public sealed class WorkbookChangedEventArgs(IReadOnlyList<SheetRegion> regions, bool structureChanged, bool sheetsChanged) : EventArgs
{
    public IReadOnlyList<SheetRegion> Regions { get; } = regions;

    /// <summary>Rows or columns were inserted, deleted, resized or hidden, or merges changed: redraw everything of the sheet.</summary>
    public bool StructureChanged { get; } = structureChanged;

    /// <summary>Sheets were added, removed, renamed, moved or recoloured (the tab bar).</summary>
    public bool SheetsChanged { get; } = sheetsChanged;
}

/// <summary>
/// A workbook: worksheets in order, the shared style table, the theme colours, defined names, the date system, the
/// calculation settings and engine, and – for a workbook read from a file – the original package with everything the model
/// does not own (charts, pictures, pivot tables, macros …; <see cref="UnsupportedContent"/> names them in German).
/// Changes collect until <see cref="PublishChanges"/> raises <see cref="Changed"/> once (the editing session does after
/// every command and recalculation).
/// </summary>
public sealed class Workbook
{
    private const int MaxRegions = 256;

    private readonly List<Worksheet> _sheets = [];
    private readonly List<SheetRegion> _changes = [];
    private int _nextSheetId = 1;
    private int _nextChartId = 1;
    private bool _structureChanged;
    private bool _sheetsChanged;

    public Workbook(CellStyle? defaultStyle = null)
    {
        Styles = new StyleTable(defaultStyle ?? CellStyle.Default);
        Calculation = new CalcEngine(this);
    }

    /// <summary>Raised by <see cref="PublishChanges"/> when something changed.</summary>
    public event EventHandler<WorkbookChangedEventArgs>? Changed;

    public IReadOnlyList<Worksheet> Sheets => _sheets;

    public StyleTable Styles { get; }

    public ThemeColors Theme { get; set; } = ThemeColors.Office;

    /// <summary>The defined names (Namens-Manager); after changing them call <see cref="CalcEngine.Rebuild"/>.</summary>
    public List<DefinedName> Names { get; } = [];

    /// <summary>The PivotCaches of the workbook's PivotTables (by <see cref="Pivot.PivotCache.Id"/>).</summary>
    public List<Pivot.PivotCache> PivotCaches { get; } = [];

    public Pivot.PivotCache? FindPivotCache(int id) => PivotCaches.FirstOrDefault(cache => cache.Id == id);

    /// <summary>The 1904 date system (serial 0 = 1904-01-01) instead of 1900.</summary>
    public bool Date1904 { get; set; }

    public CalculationSettings CalculationSettings { get; set; } = new();

    public CalcEngine Calculation { get; }

    public int ActiveSheetIndex { get; set; }

    public Worksheet? ActiveSheet => ActiveSheetIndex >= 0 && ActiveSheetIndex < _sheets.Count ? _sheets[ActiveSheetIndex] : null;

    /// <summary>What the file had that ThixCalc keeps but does not show or edit (German, for the opening warning).</summary>
    public IReadOnlyList<string> UnsupportedContent { get; internal set; } = [];

    /// <summary>The package the workbook was read from (the writer starts from it), or null for a new workbook.</summary>
    internal byte[]? OriginalPackage { get; set; }

    /// <summary>Sheets that are no worksheets (chart sheets …), as their workbook.xml entries with their original position.</summary>
    internal List<(int Position, string Xml)> PreservedSheets { get; set; } = [];

    /// <summary>The package has a VBA project (.xlsm): kept, never run.</summary>
    public bool HasMacros { get; internal set; }

    /// <summary>A new workbook with one sheet "Tabelle1".</summary>
    public static Workbook Create(string sheetName = "Tabelle1")
    {
        var workbook = new Workbook();
        workbook.AddSheet(sheetName);
        return workbook;
    }

    public int IndexOf(Worksheet sheet) => _sheets.IndexOf(sheet);

    /// <summary>A chart id not used yet in this workbook.</summary>
    public int NextChartId() => _nextChartId++;

    /// <summary>The chart with <paramref name="id"/> and its sheet, or null.</summary>
    public (Worksheet Sheet, SheetChart Chart)? FindChart(int id)
    {
        foreach (var sheet in _sheets)
        {
            foreach (var chart in sheet.Charts)
            {
                if (chart.Id == id)
                {
                    return (sheet, chart);
                }
            }
        }

        return null;
    }

    public Worksheet? FindSheet(string name) =>
        _sheets.FirstOrDefault(sheet => string.Equals(sheet.Name, name, StringComparison.OrdinalIgnoreCase));

    public Worksheet? GetSheetById(int id)
    {
        foreach (var sheet in _sheets)
        {
            if (sheet.Id == id)
            {
                return sheet;
            }
        }

        return null;
    }

    public Worksheet this[string name] => FindSheet(name) ?? throw new KeyNotFoundException(name);

    /// <summary>"Tabelle2" … the first free name with <paramref name="baseName"/>.</summary>
    public string NextSheetName(string baseName = "Tabelle")
    {
        for (var number = _sheets.Count + 1; ; number++)
        {
            var name = baseName + number;
            if (FindSheet(name) is null)
            {
                return name;
            }
        }
    }

    /// <summary>Adds a sheet (at the end or at <paramref name="index"/>); the name is checked like Excel does.</summary>
    public Worksheet AddSheet(string? name = null, int? index = null)
    {
        name ??= NextSheetName();
        ValidateSheetName(name, null);
        var sheet = new Worksheet(this, _nextSheetId++, name);
        _sheets.Insert(index ?? _sheets.Count, sheet);
        _sheetsChanged = true;
        Calculation.OnSheetsChanged(sheet.Name);
        return sheet;
    }

    /// <summary>Renames a sheet; formulas refer to it by id, so their text follows.</summary>
    public void RenameSheet(Worksheet sheet, string name)
    {
        ValidateSheetName(name, sheet);
        var old = sheet.Name;
        sheet.Name = name;

        // Charts name their data by sheet name; formulas by id.
        foreach (var other in _sheets)
        {
            for (var index = 0; index < other.Charts.Count; index++)
            {
                var chart = other.Charts[index];
                var renamed = ThixOffice.Charts.ChartEdits.MapReferences(chart.Chart, formula => ChartReferences.Rename(formula, old, name));
                if (!ThixOffice.Charts.ChartEdits.References(renamed).SequenceEqual(ThixOffice.Charts.ChartEdits.References(chart.Chart)))
                {
                    other.Charts[index] = chart with { Chart = renamed };
                }
            }
        }

        _sheetsChanged = true;
        Calculation.OnSheetsChanged(name);
    }

    public void MoveSheet(Worksheet sheet, int index)
    {
        _sheets.Remove(sheet);
        _sheets.Insert(Math.Clamp(index, 0, _sheets.Count), sheet);
        _sheetsChanged = true;
        Calculation.OnSheetsChanged(null);
    }

    /// <summary>Removes a sheet; references to it become #REF!, names scoped to it go.</summary>
    public void RemoveSheet(Worksheet sheet)
    {
        if (_sheets.Count <= 1)
        {
            throw new BusinessException("Eine Arbeitsmappe muss mindestens ein sichtbares Blatt enthalten.");
        }

        var index = _sheets.IndexOf(sheet);
        ReferenceRewriter.ApplyToWorkbook(this, new SheetDeletion(sheet.Id, this));
        _sheets.Remove(sheet);
        Names.RemoveAll(name => name.SheetId == sheet.Id);
        if (ActiveSheetIndex >= _sheets.Count || ActiveSheetIndex > index)
        {
            ActiveSheetIndex = Math.Max(0, ActiveSheetIndex - 1);
        }

        _sheetsChanged = true;
        Calculation.Rebuild();
    }

    /// <summary>Removes a sheet whose references were already rewritten (undo of adding it).</summary>
    internal void RemoveSheetWithoutChecks(Worksheet sheet)
    {
        _sheets.Remove(sheet);
        ActiveSheetIndex = Math.Clamp(ActiveSheetIndex, 0, Math.Max(0, _sheets.Count - 1));
        _sheetsChanged = true;
    }

    /// <summary>Puts a removed sheet back (undo of a deletion) at <paramref name="index"/>.</summary>
    internal void InsertExistingSheet(Worksheet sheet, int index)
    {
        _sheets.Insert(Math.Clamp(index, 0, _sheets.Count), sheet);
        _sheetsChanged = true;
    }

    /// <summary>The defined name visible from <paramref name="sheet"/>: a local one first, then the workbook's.</summary>
    public DefinedName? FindName(string name, Worksheet? sheet)
    {
        DefinedName? global = null;
        foreach (var defined in Names)
        {
            if (!string.Equals(defined.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (defined.SheetId is null)
            {
                global = defined;
            }
            else if (sheet is not null && defined.SheetId == sheet.Id)
            {
                return defined;
            }
        }

        return global;
    }

    /// <summary>Checks a sheet name: 1–31 characters, none of : \ / ? * [ ], not starting or ending with ', unique.</summary>
    public void ValidateSheetName(string name, Worksheet? self)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessException("Der Blattname darf nicht leer sein.", "name");
        }

        if (name.Length > 31)
        {
            throw new BusinessException("Der Blattname darf höchstens 31 Zeichen lang sein.", "name");
        }

        if (name.IndexOfAny([':', '\\', '/', '?', '*', '[', ']']) >= 0 || name.StartsWith('\'') || name.EndsWith('\''))
        {
            throw new BusinessException("Der Blattname darf die Zeichen : \\ / ? * [ ] nicht enthalten und nicht mit ' beginnen oder enden.", "name");
        }

        if (string.Equals(name, "Verlauf", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "History", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("„Verlauf“ ist als Blattname reserviert.", "name");
        }

        var existing = FindSheet(name);
        if (existing is not null && existing != self)
        {
            throw new BusinessException($"Es gibt bereits ein Blatt namens „{name}“.", "name");
        }
    }

    /// <summary>Raises <see cref="Changed"/> with what collected since the last call (nothing when nothing changed).</summary>
    public void PublishChanges()
    {
        if (_changes.Count == 0 && !_structureChanged && !_sheetsChanged)
        {
            return;
        }

        var args = new WorkbookChangedEventArgs(_changes.ToArray(), _structureChanged, _sheetsChanged);
        _changes.Clear();
        _structureChanged = false;
        _sheetsChanged = false;
        Changed?.Invoke(this, args);
    }

    /// <summary>Discards collected changes (after loading).</summary>
    internal void ForgetChanges()
    {
        _changes.Clear();
        _structureChanged = false;
        _sheetsChanged = false;
    }

    internal void NoteChanged(Worksheet sheet, CellRange range, bool styleOnly = false)
    {
        if (Changed is null)
        {
            return;
        }

        if (_changes.Count > 0)
        {
            var last = _changes[^1];
            if (last.Sheet == sheet && last.StyleOnly == styleOnly && (last.Range.Contains(range) || Adjacent(last.Range, range)))
            {
                _changes[^1] = last with { Range = last.Range.Union(range) };
                return;
            }
        }

        if (_changes.Count >= MaxRegions)
        {
            // Too many small regions: one bounding region per sheet is cheaper for the grid.
            var merged = _changes.Where(change => change.Sheet == sheet).Aggregate(range, (all, change) => all.Union(change.Range));
            _changes.RemoveAll(change => change.Sheet == sheet);
            _changes.Add(new SheetRegion(sheet, merged));
            return;
        }

        _changes.Add(new SheetRegion(sheet, range, styleOnly));
    }

    internal void NoteStructureChanged() => _structureChanged = true;

    internal void NoteSheetsChanged() => _sheetsChanged = true;

    private static bool Adjacent(CellRange first, CellRange second) =>
        (first.FirstColumn == second.FirstColumn && first.LastColumn == second.LastColumn && second.FirstRow == first.LastRow + 1)
        || (first.FirstRow == second.FirstRow && first.LastRow == second.LastRow && second.FirstColumn == first.LastColumn + 1);
}
