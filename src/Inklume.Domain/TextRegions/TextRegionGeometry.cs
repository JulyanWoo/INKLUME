namespace Inklume.Domain.TextRegions;

public abstract class TextRegionGeometry
{
    private const double BoundsTolerance = 0.000001;

    public abstract TextRegionGeometryKind Kind { get; }

    public abstract IReadOnlyList<ImagePoint> Points { get; }

    public ImageBounds Bounds
    {
        get
        {
            double minimumX = Points.Min(point => point.X);
            double minimumY = Points.Min(point => point.Y);
            double maximumX = Points.Max(point => point.X);
            double maximumY = Points.Max(point => point.Y);
            return new ImageBounds(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY);
        }
    }

    public bool IsWithin(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return false;
        }

        return Points.All(point => point.X >= -BoundsTolerance
            && point.Y >= -BoundsTolerance
            && point.X <= pixelWidth + BoundsTolerance
            && point.Y <= pixelHeight + BoundsTolerance);
    }
}
