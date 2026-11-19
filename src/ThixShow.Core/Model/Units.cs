namespace ThixShow.Core.Model;

/// <summary>
/// DrawingML's units. The model keeps the file's integers so that a round trip is exact: lengths in EMU (English Metric Units,
/// 914400 per inch, 12700 per point), angles in 60000ths of a degree, percentages in 1000ths of a percent (100000 = 100 %),
/// font sizes in hundredths of a point. The layout works in points (doubles).
/// </summary>
public static class Units
{
    public const long EmuPerInch = 914400;
    public const long EmuPerPoint = 12700;
    public const long EmuPerCentimeter = 360000;
    public const int AnglePerDegree = 60000;
    public const int FullAngle = 21600000;
    public const int Percent100 = 100000;

    public static double EmuToPoints(long emu) => emu / (double)EmuPerPoint;

    public static long PointsToEmu(double points) => (long)Math.Round(points * EmuPerPoint);

    public static double EmuToCentimeters(long emu) => emu / (double)EmuPerCentimeter;

    public static long CentimetersToEmu(double centimeters) => (long)Math.Round(centimeters * EmuPerCentimeter);

    public static double AngleToDegrees(int angle) => angle / (double)AnglePerDegree;

    public static int DegreesToAngle(double degrees) => (int)Math.Round(degrees * AnglePerDegree);

    /// <summary>A percentage (100000 = 1.0) as a factor.</summary>
    public static double PercentToFactor(int percent) => percent / (double)Percent100;

    public static int FactorToPercent(double factor) => (int)Math.Round(factor * Percent100);

    /// <summary>Normalizes an angle into [0, 21600000).</summary>
    public static int NormalizeAngle(int angle)
    {
        var value = angle % FullAngle;
        return value < 0 ? value + FullAngle : value;
    }
}

/// <summary>A point in EMU.</summary>
public readonly record struct EmuPoint(long X, long Y);

/// <summary>A size in EMU.</summary>
public readonly record struct EmuSize(long Width, long Height);

/// <summary>A rectangle in EMU.</summary>
public readonly record struct EmuRect(long X, long Y, long Width, long Height)
{
    public long Right => X + Width;

    public long Bottom => Y + Height;

    public long CenterX => X + (Width / 2);

    public long CenterY => Y + (Height / 2);

    public bool Contains(long x, long y) => x >= X && x <= Right && y >= Y && y <= Bottom;

    public bool IntersectsWith(EmuRect other) => other.X <= Right && other.Right >= X && other.Y <= Bottom && other.Bottom >= Y;

    public static EmuRect Union(EmuRect first, EmuRect second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        return new EmuRect(left, top, Math.Max(first.Right, second.Right) - left, Math.Max(first.Bottom, second.Bottom) - top);
    }
}

/// <summary>
/// Insets of a rectangle as fractions in 1000ths of a percent (a:srcRect, a:fillRect, a:fillToRect): positive values cut in,
/// negative values extend.
/// </summary>
public readonly record struct RelativeRect(int Left, int Top, int Right, int Bottom)
{
    public static readonly RelativeRect Zero = new(0, 0, 0, 0);

    public bool IsZero => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
}
