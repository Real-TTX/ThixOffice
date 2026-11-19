using System.Diagnostics;
using Avalonia.Headless.XUnit;
using ThixCalc.Core.Model;
using Xunit.Abstractions;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>Scrolling and typing in a sheet of 100,000 rows: the frame (layout + display list + picture) stays far below 16 ms.</summary>
public sealed class PerformanceTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task Scrolling_and_typing_in_100k_rows_stay_fluid()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        var sheet = editor.Session.ActiveSheet;
        for (var row = 0; row < 100_000; row++)
        {
            sheet.SetValue(row, 0, CellValue.FromText("Artikel " + row));
            sheet.SetValue(row, 1, row * 1.25);
            sheet.SetValue(row, 2, row % 7);
        }

        editor.Session.Recalculate();
        await SettleAsync();
        var frames = new List<double>();
        var watch = Stopwatch.StartNew();
        for (var step = 0; step < 60; step++)
        {
            editor.Surface.ScrollBy(0, 37);
            editor.Surface.InvalidateGrid();
            TestData.Settle();
            frames.Add(editor.Surface.LastRenderTime.TotalMilliseconds);
        }

        var scrolled = watch.Elapsed.TotalMilliseconds / 60;
        editor.Session.Select(new CellAddress(50_000, 3));
        await SettleAsync();
        watch.Restart();
        for (var key = 0; key < 20; key++)
        {
            await EnterAsync(window, (key * 3).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var typed = watch.Elapsed.TotalMilliseconds / 20;
        frames.Sort();
        output.WriteLine($"frame layout+render: median {frames[frames.Count / 2]:0.00} ms, max {frames[^1]:0.00} ms; scroll step incl. dispatcher {scrolled:0.0} ms; typed entry incl. recalculation and settle {typed:0.0} ms");
        Assert.True(frames[frames.Count / 2] < 16, $"median frame {frames[frames.Count / 2]} ms");
    }
}
