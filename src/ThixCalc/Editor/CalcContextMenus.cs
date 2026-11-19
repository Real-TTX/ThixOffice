using Avalonia.Controls;
using ThixCalc.Core.Layout;
using ThixOffice.Desktop.Shell;

namespace ThixCalc.Editor;

/// <summary>The right-click menus of the grid, like Excel's: for cells, for row headers and for column headers.</summary>
internal static class CalcContextMenus
{
    private static readonly OfficeCommandMenu.Submenu PasteSpecial = new("Inhalte einfügen", "paste",
    [
        CalcCommandIds.PasteValues, CalcCommandIds.PasteFormulas, CalcCommandIds.PasteFormats, CalcCommandIds.PasteValuesAndFormats,
        CalcCommandIds.PasteNoBorders, CalcCommandIds.PasteTranspose, null, CalcCommandIds.PasteSpecial,
    ]);

    private static readonly OfficeCommandMenu.Submenu SortMenu = new("Sortieren", "sort",
        [CalcCommandIds.SortAscending, CalcCommandIds.SortDescending, null, CalcCommandIds.SortCustom]);

    /// <summary>The menu of a chart: clipboard, the chart's type and data, its order, deleting it.</summary>
    public static ContextMenu ForChart(OfficeWindow window) => OfficeCommandMenu.Create(window,
    [
        CalcCommandIds.Cut, CalcCommandIds.Copy, CalcCommandIds.Paste, null,
        CalcCommandIds.ChartChangeType, CalcCommandIds.ChartSelectData, CalcCommandIds.ChartSwitchRowColumn, null,
        CalcCommandIds.ChartBringToFront, CalcCommandIds.ChartSendToBack, null,
        CalcCommandIds.ChartDelete,
    ]);

    /// <summary>The menu of a PivotTable cell (Excel's: copy, format, refresh, sort, value field settings, details, grouping, field list).</summary>
    public static ContextMenu ForPivot(OfficeWindow window) => OfficeCommandMenu.Create(window,
    [
        CalcCommandIds.Copy, CalcCommandIds.FormatCells, null,
        CalcCommandIds.PivotRefresh, new OfficeCommandMenu.Submenu("Sortieren", "sort", [CalcCommandIds.PivotSortAscending, CalcCommandIds.PivotSortDescending]), null,
        CalcCommandIds.PivotValueSettings, CalcCommandIds.PivotFieldSettings, CalcCommandIds.PivotDetails, null,
        CalcCommandIds.PivotGroup, CalcCommandIds.PivotUngroup, null,
        CalcCommandIds.PivotFieldList, CalcCommandIds.PivotDelete,
    ],
    [CalcCommandIds.PivotValueSettings, CalcCommandIds.PivotFieldSettings, CalcCommandIds.PivotDetails, CalcCommandIds.PivotGroup, CalcCommandIds.PivotUngroup]);

    public static ContextMenu For(OfficeWindow window, HitKind kind) => kind switch
    {
        HitKind.RowHeader or HitKind.RowResize => OfficeCommandMenu.Create(window,
        [
            CalcCommandIds.Cut, CalcCommandIds.Copy, CalcCommandIds.Paste, PasteSpecial, null,
            CalcCommandIds.InsertRows, CalcCommandIds.DeleteRows, CalcCommandIds.ClearContents, null,
            CalcCommandIds.FormatCells, CalcCommandIds.RowHeight, CalcCommandIds.AutoFitRows, CalcCommandIds.HideRows, CalcCommandIds.UnhideRows,
        ]),
        HitKind.ColumnHeader or HitKind.ColumnResize => OfficeCommandMenu.Create(window,
        [
            CalcCommandIds.Cut, CalcCommandIds.Copy, CalcCommandIds.Paste, PasteSpecial, null,
            CalcCommandIds.InsertColumns, CalcCommandIds.DeleteColumns, CalcCommandIds.ClearContents, null,
            CalcCommandIds.FormatCells, CalcCommandIds.ColumnWidth, CalcCommandIds.AutoFitColumns, CalcCommandIds.HideColumns, CalcCommandIds.UnhideColumns,
        ]),
        _ => OfficeCommandMenu.Create(window,
        [
            CalcCommandIds.Cut, CalcCommandIds.Copy, CalcCommandIds.Paste, PasteSpecial, null,
            CalcCommandIds.InsertCells, CalcCommandIds.DeleteCells, CalcCommandIds.ClearContents, null,
            SortMenu, CalcCommandIds.Filter, null,
            CalcCommandIds.Comment, CalcCommandIds.DeleteCommentReview, CalcCommandIds.FormatCells, CalcCommandIds.Hyperlink, CalcCommandIds.DefineName,
        ],
        [CalcCommandIds.DeleteCommentReview]),
    };
}
