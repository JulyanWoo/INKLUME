namespace Inklume.Domain.TextRegions;

public sealed class PolygonTextRegionGeometry : TextRegionGeometry
{
    private const double AreaTolerance = 0.000001;
    private readonly IReadOnlyList<ImagePoint> _points;

    public PolygonTextRegionGeometry(IEnumerable<ImagePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        ImagePoint[] pointArray = [.. points];
        if (pointArray.Length < 3)
        {
            throw new ArgumentException("A polygon requires at least three points.", nameof(points));
        }

        if (Math.Abs(CalculateSignedDoubleArea(pointArray)) <= AreaTolerance)
        {
            throw new ArgumentException("A polygon must have a non-degenerate area.", nameof(points));
        }

        if (HasSelfIntersection(pointArray))
        {
            throw new ArgumentException("A self-intersecting polygon is not supported.", nameof(points));
        }

        _points = Array.AsReadOnly(pointArray);
    }

    public override TextRegionGeometryKind Kind => TextRegionGeometryKind.Polygon;

    public override IReadOnlyList<ImagePoint> Points => _points;

    private static double CalculateSignedDoubleArea(IReadOnlyList<ImagePoint> points)
    {
        double area = 0;
        for (int index = 0; index < points.Count; index++)
        {
            ImagePoint current = points[index];
            ImagePoint next = points[(index + 1) % points.Count];
            area += (current.X * next.Y) - (next.X * current.Y);
        }

        return area;
    }

    private static bool HasSelfIntersection(IReadOnlyList<ImagePoint> points)
    {
        for (int first = 0; first < points.Count; first++)
        {
            int firstNext = (first + 1) % points.Count;
            for (int second = first + 1; second < points.Count; second++)
            {
                int secondNext = (second + 1) % points.Count;
                if (first == second || firstNext == second || secondNext == first)
                {
                    continue;
                }

                if (SegmentsIntersect(points[first], points[firstNext], points[second], points[secondNext]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(ImagePoint a, ImagePoint b, ImagePoint c, ImagePoint d)
    {
        double first = Cross(a, b, c);
        double second = Cross(a, b, d);
        double third = Cross(c, d, a);
        double fourth = Cross(c, d, b);
        return ((first > AreaTolerance && second < -AreaTolerance) || (first < -AreaTolerance && second > AreaTolerance))
            && ((third > AreaTolerance && fourth < -AreaTolerance) || (third < -AreaTolerance && fourth > AreaTolerance));
    }

    private static double Cross(ImagePoint origin, ImagePoint first, ImagePoint second)
        => ((first.X - origin.X) * (second.Y - origin.Y))
            - ((first.Y - origin.Y) * (second.X - origin.X));
}
