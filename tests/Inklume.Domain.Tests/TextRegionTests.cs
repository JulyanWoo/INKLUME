using Inklume.Domain.TextRegions;
using Xunit;

namespace Inklume.Domain.Tests;

public sealed class TextRegionTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ImagePoint_ShouldRejectNonFiniteCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImagePoint(double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImagePoint(1, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImagePoint(double.PositiveInfinity, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImagePoint(1, double.NegativeInfinity));
    }

    [Fact]
    public void Rectangle_ShouldExposeCanonicalPointsAndRejectZeroArea()
    {
        var rectangle = new RectangleTextRegionGeometry(10.5, 20.25, 30, 40);

        Assert.Equal(TextRegionGeometryKind.Rectangle, rectangle.Kind);
        Assert.Equal(
            [new ImagePoint(10.5, 20.25), new ImagePoint(40.5, 20.25),
                new ImagePoint(40.5, 60.25), new ImagePoint(10.5, 60.25)],
            rectangle.Points);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectangleTextRegionGeometry(0, 0, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectangleTextRegionGeometry(0, 0, 10, -1));
    }

    [Fact]
    public void Polygon_ShouldRejectInsufficientDegenerateAndSelfIntersectingPoints()
    {
        Assert.Throws<ArgumentException>(() => new PolygonTextRegionGeometry(
            [new ImagePoint(0, 0), new ImagePoint(1, 1)]));
        Assert.Throws<ArgumentException>(() => new PolygonTextRegionGeometry(
            [new ImagePoint(0, 0), new ImagePoint(1, 1), new ImagePoint(2, 2)]));
        Assert.Throws<ArgumentException>(() => new PolygonTextRegionGeometry(
            [new ImagePoint(0, 0), new ImagePoint(10, 10), new ImagePoint(0, 10), new ImagePoint(10, 0)]));
    }

    [Fact]
    public void GeometryBounds_ShouldUseRawImageCoordinates()
    {
        var polygon = new PolygonTextRegionGeometry(
            [new ImagePoint(50, 20), new ImagePoint(100, 40), new ImagePoint(70, 90)]);

        Assert.Equal(new ImageBounds(50, 20, 50, 70), polygon.Bounds);
        Assert.True(polygon.IsWithin(1080, 15000));
        Assert.False(polygon.IsWithin(80, 15000));
    }

    [Fact]
    public void TextRegion_ShouldUseUnknownClassificationAndPositiveReadingOrder()
    {
        var geometry = new RectangleTextRegionGeometry(10, 20, 30, 40);
        var region = new TextRegion(
            Guid.NewGuid(), Guid.NewGuid(), geometry, 1,
            TextRegionRole.Unknown, TextContainerType.Unknown, Timestamp, Timestamp);

        Assert.Equal(TextRegionRole.Unknown, region.Role);
        Assert.Equal(TextContainerType.Unknown, region.ContainerType);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextRegion(
            Guid.NewGuid(), Guid.NewGuid(), geometry, 0,
            TextRegionRole.Unknown, TextContainerType.Unknown, Timestamp, Timestamp));
    }
}
