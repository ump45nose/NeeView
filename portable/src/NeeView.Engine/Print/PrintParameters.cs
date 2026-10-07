namespace NeeView;

public enum PrintMode { RawImage, View, ViewFill, ViewStretch }
public enum PrintOrientation { Portrait, Landscape }
public enum PrintHorizontalAlignment { Left, Center, Right }
public enum PrintVerticalAlignment { Top, Center, Bottom }

public readonly record struct PrintMargin(double Left, double Top, double Right, double Bottom)
{
    public static PrintMargin Zero => new();
}

public sealed record PrintParameters
{
    public PrintMode Mode { get; init; } = PrintMode.RawImage;
    public PrintOrientation Orientation { get; init; } = PrintOrientation.Portrait;
    public bool IsBackground { get; init; }
    public bool IsDotScale { get; init; }
    public int Columns { get; init; } = 1;
    public int Rows { get; init; } = 1;
    public PrintHorizontalAlignment HorizontalAlignment { get; init; } = PrintHorizontalAlignment.Center;
    public PrintVerticalAlignment VerticalAlignment { get; init; } = PrintVerticalAlignment.Center;
    public PrintMargin MarginMm { get; init; } = PrintMargin.Zero;
}
