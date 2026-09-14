namespace Inklume.Domain.TextRegions;

public readonly record struct ImagePoint
{
    public ImagePoint(double x, double y)
    {
        if (!double.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Image coordinates must be finite.");
        }

        if (!double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Image coordinates must be finite.");
        }

        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }
}
