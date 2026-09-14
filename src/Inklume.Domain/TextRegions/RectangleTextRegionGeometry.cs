namespace Inklume.Domain.TextRegions;

public sealed class RectangleTextRegionGeometry : TextRegionGeometry
{
    private readonly IReadOnlyList<ImagePoint> _points;

    public RectangleTextRegionGeometry(double x, double y, double width, double height)
    {
        var topLeft = new ImagePoint(x, y);
        if (!double.IsFinite(width) || width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Rectangle width must be finite and positive.");
        }

        if (!double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Rectangle height must be finite and positive.");
        }

        if (!double.IsFinite(x + width) || !double.IsFinite(y + height))
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Rectangle coordinates must be finite.");
        }

        X = topLeft.X;
        Y = topLeft.Y;
        Width = width;
        Height = height;
        _points = Array.AsReadOnly(
        [
            topLeft,
            new ImagePoint(x + width, y),
            new ImagePoint(x + width, y + height),
            new ImagePoint(x, y + height)
        ]);
    }

    public override TextRegionGeometryKind Kind => TextRegionGeometryKind.Rectangle;

    public override IReadOnlyList<ImagePoint> Points => _points;

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }
}
