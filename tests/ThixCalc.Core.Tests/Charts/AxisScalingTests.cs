using ThixOffice.Charts;

namespace ThixCalc.Core.Tests.Charts;

/// <summary>Automatic axis scales: the table of Excel-like choices (zero start, 5 % headroom, 1-2-5 units, fixed parts kept).</summary>
public class AxisScalingTests
{
    [Theory]
    [InlineData(0, 100, 6, 0, 120, 20)]
    [InlineData(0, 100, 10, 0, 120, 20)]
    [InlineData(10, 90, 5, 0, 100, 20)]
    [InlineData(3, 47, 5, 0, 50, 10)]
    [InlineData(0, 1, 6, 0, 1.2, 0.2)]
    [InlineData(0.2, 0.9, 5, 0, 1, 0.2)]
    [InlineData(1200, 4800, 6, 0, 6000, 1000)]
    [InlineData(950, 1000, 5, 920, 1020, 20)]
    [InlineData(-30, 70, 6, -40, 80, 20)]
    [InlineData(-80, -20, 5, -100, 0, 20)]
    [InlineData(5, 5, 5, 0, 6, 2)]
    [InlineData(0, 0, 6, 0, 1.2, 0.2)]
    [InlineData(-3, -3, 5, -4, 0, 1)]
    public void Automatic_scales_look_like_Excel(double minimum, double maximum, int intervals, double expectedMinimum, double expectedMaximum, double expectedUnit)
    {
        var scale = AxisScaling.ValueScale(minimum, maximum, intervals);

        Assert.Equal(expectedMinimum, scale.Minimum, 9);
        Assert.Equal(expectedMaximum, scale.Maximum, 9);
        Assert.Equal(expectedUnit, scale.MajorUnit, 9);
    }

    [Theory]
    [InlineData(0.7, 1)]
    [InlineData(1, 1)]
    [InlineData(1.1, 2)]
    [InlineData(2.5, 5)]
    [InlineData(7, 10)]
    [InlineData(0.034, 0.05)]
    [InlineData(17.5, 20)]
    [InlineData(260, 500)]
    public void Units_are_one_two_or_five_times_a_power_of_ten(double rough, double expected) =>
        Assert.Equal(expected, AxisScaling.NiceUnit(rough), 12);

    [Fact]
    public void Fixed_minimum_maximum_and_unit_are_kept()
    {
        var scale = AxisScaling.ValueScale(3, 47, 5, fixedMinimum: 10, fixedMaximum: 40, fixedUnit: 7.5);

        Assert.Equal((10, 40, 7.5), (scale.Minimum, scale.Maximum, scale.MajorUnit));
        Assert.Equal([10, 17.5, 25, 32.5, 40], scale.Ticks);
    }

    [Fact]
    public void Ticks_have_no_float_noise()
    {
        var scale = AxisScaling.ValueScale(0, 0.95, 10);

        Assert.All(scale.Ticks, tick => Assert.Equal(Math.Round(tick, 6), tick));
        Assert.Contains(0.3, scale.Ticks);
    }

    [Fact]
    public void Fewer_intervals_give_a_coarser_unit()
    {
        var dense = AxisScaling.ValueScale(0, 1000, 10);
        var sparse = AxisScaling.ValueScale(0, 1000, 2);

        Assert.True(sparse.MajorUnit > dense.MajorUnit);
        Assert.True(sparse.Ticks.Count <= 3);
    }

    [Fact]
    public void Percent_axes_run_to_one_hundred_percent()
    {
        var scale = AxisScaling.PercentScale(hasNegative: false, maxIntervals: 5);

        Assert.Equal((0, 1, 0.2), (scale.Minimum, scale.Maximum, scale.MajorUnit));
    }

    [Fact]
    public void Time_axes_find_their_unit()
    {
        // 2024-01-01 = 45292, 2024-02-01 = 45323, 2024-03-01 = 45352.
        Assert.Equal(DateUnit.Months, AxisScaling.BaseUnit([45292, 45323, 45352]));
        Assert.Equal(DateUnit.Days, AxisScaling.BaseUnit([45292, 45293, 45300]));
        Assert.Equal(DateUnit.Years, AxisScaling.BaseUnit([45292, 45658]));
        Assert.Equal(2, AxisScaling.DateOffset(45352, 45292, DateUnit.Months));
        Assert.Equal(45352, AxisScaling.AddUnits(45292, 2, DateUnit.Months));
        Assert.Equal(6, AxisScaling.DateMajorUnit(23, DateUnit.Months, 8));
        Assert.Equal(3, AxisScaling.DateMajorUnit(23, DateUnit.Months, 9));
    }
}
