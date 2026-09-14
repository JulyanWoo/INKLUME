using Inklume.Desktop.VisualEditor;
using Inklume.Domain.TextRegions;
using Xunit;

namespace Inklume.Desktop.Tests;

public sealed class ViewportTransformTests
{
    [Theory]
    [InlineData(0.30, 0, 0)]
    [InlineData(1.00, 75, -40)]
    [InlineData(2.00, -120, 90)]
    public void ImageAndViewportMappings_ShouldRoundTripAcrossZoomAndPan(
        double zoom,
        double horizontalPan,
        double verticalPan)
    {
        ViewportTransform transform = ViewportTransform.Fit(1080, 15000, 1200, 800)
            .ZoomAt(new ViewportPoint(600, 400), zoom)
            .PanBy(horizontalPan, verticalPan);
        var rawPoint = new ImagePoint(420.5, 7310.2);

        ViewportPoint viewportPoint = transform.ImageToViewport(rawPoint);
        ImagePoint restored = transform.ViewportToImage(viewportPoint);

        Assert.Equal(rawPoint.X, restored.X, precision: 8);
        Assert.Equal(rawPoint.Y, restored.Y, precision: 8);
    }

    [Fact]
    public void ZoomAt_ShouldKeepRawPointUnderCursorInvariant()
    {
        ViewportTransform before = ViewportTransform.Fit(1080, 2400, 1000, 700).Reset();
        var cursor = new ViewportPoint(430, 310);
        ImagePoint rawBefore = before.ViewportToImage(cursor);

        ViewportTransform after = before.ZoomAt(cursor, 1.7);
        ImagePoint rawAfter = after.ViewportToImage(cursor);

        Assert.Equal(rawBefore.X, rawAfter.X, precision: 8);
        Assert.Equal(rawBefore.Y, rawAfter.Y, precision: 8);
    }

    [Fact]
    public void Fit_ShouldAllowLongVerticalPageBelowInteractiveMinimum()
    {
        ViewportTransform transform = ViewportTransform.Fit(1080, 15000, 1000, 700);

        Assert.InRange(transform.Zoom, 0.01, ViewportTransform.MinimumInteractiveZoom);
        Assert.Equal((1000 - (1080 * transform.Zoom)) / 2, transform.OffsetX, precision: 8);
        Assert.Equal((700 - (15000 * transform.Zoom)) / 2, transform.OffsetY, precision: 8);
    }

    [Fact]
    public void PreviewToImage_ShouldAccountForDecodedPreviewScale()
    {
        ViewportTransform transform = ViewportTransform.Fit(1080, 15000, 1000, 700);

        ImagePoint raw = transform.PreviewToImage(new ImagePoint(270, 3750), 540, 7500);

        Assert.Equal(new ImagePoint(540, 7500), raw);
    }
}
