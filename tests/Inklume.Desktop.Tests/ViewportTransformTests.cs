using Inklume.Desktop.VisualEditor;
using Inklume.Domain.TextRegions;
using Xunit;

namespace Inklume.Desktop.Tests;

public sealed class ViewportTransformTests
{
    [Theory]
    [InlineData(0.10, 0, 0)]
    [InlineData(0.30, 0, 0)]
    [InlineData(1.00, 75, -40)]
    [InlineData(2.00, -120, 90)]
    [InlineData(8.00, 500, -300)]
    [InlineData(32.00, -1500, 2000)]
    [InlineData(64.00, -5000, 8000)]
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

        Assert.Equal(rawPoint.X, restored.X, precision: 6);
        Assert.Equal(rawPoint.Y, restored.Y, precision: 6);
    }

    [Theory]
    [InlineData(1.7)]
    [InlineData(8.0)]
    [InlineData(32.0)]
    [InlineData(64.0)]
    public void ZoomAt_ShouldKeepRawPointUnderCursorInvariant(double targetZoom)
    {
        ViewportTransform before = ViewportTransform.Fit(1080, 2400, 1000, 700).Reset();
        var cursor = new ViewportPoint(430, 310);
        ImagePoint rawBefore = before.ViewportToImage(cursor);

        ViewportTransform after = before.ZoomAt(cursor, targetZoom);
        ImagePoint rawAfter = after.ViewportToImage(cursor);

        Assert.Equal(rawBefore.X, rawAfter.X, precision: 6);
        Assert.Equal(rawBefore.Y, rawAfter.Y, precision: 6);
    }

    [Fact]
    public void Fit_ShouldAllowLongVerticalPageBelowInteractiveMinimum()
    {
        ViewportTransform transform = ViewportTransform.Fit(1080, 15000, 1000, 700);

        Assert.InRange(transform.Zoom, 0.0001, ViewportTransform.MinimumInteractiveZoom);
        Assert.Equal((1000 - (1080 * transform.Zoom)) / 2, transform.OffsetX, precision: 6);
        Assert.Equal((700 - (15000 * transform.Zoom)) / 2, transform.OffsetY, precision: 6);
    }

    [Fact]
    public void Fit_ShouldAllowExtremelyLongManhwa_1080x30000_Below10Percent()
    {
        // 1080 x 30000 manhwa page fitted to standard viewport (1200 x 800 with 32px padding)
        ViewportTransform transform = ViewportTransform.Fit(1080, 30000, 1200, 800, padding: 32);

        // Available height = 800 - 64 = 736. 736 / 30000 = ~0.0245 (2.45%)
        Assert.True(transform.Zoom < ViewportTransform.MinimumInteractiveZoom,
            $"Fit zoom {transform.Zoom} should be below manual minimum {ViewportTransform.MinimumInteractiveZoom}");
        Assert.InRange(transform.Zoom, 0.02, 0.03);

        // Reset must reliably restore 100% zoom
        ViewportTransform reset = transform.Reset();
        Assert.Equal(1.00, reset.Zoom);
        Assert.Equal(1080, reset.ImageWidth);
        Assert.Equal(30000, reset.ImageHeight);
    }

    [Fact]
    public void NormalComicPage_1200x1600_FitAndMaxZoomBehavior()
    {
        ViewportTransform fit = ViewportTransform.Fit(1200, 1600, 1000, 800, padding: 32);
        Assert.True(fit.Zoom > 0.40 && fit.Zoom < 1.00);

        ViewportTransform maxZoom = fit.ZoomAt(new ViewportPoint(500, 400), 64.00);
        Assert.Equal(64.00, maxZoom.Zoom);

        // Round trip test at 6400%
        var testPoint = new ImagePoint(612.4, 855.9);
        ViewportPoint vp = maxZoom.ImageToViewport(testPoint);
        ImagePoint roundTrip = maxZoom.ViewportToImage(vp);
        Assert.Equal(testPoint.X, roundTrip.X, precision: 6);
        Assert.Equal(testPoint.Y, roundTrip.Y, precision: 6);
    }

    [Fact]
    public void PanAt6400Percent_ShouldNotOverflowOrCorruptCoordinates()
    {
        ViewportTransform transform = ViewportTransform.Fit(1080, 15000, 1200, 800)
            .ZoomAt(new ViewportPoint(600, 400), 64.00);

        // Pan significantly across the tall canvas
        ViewportTransform panned = transform.PanBy(-25000, -100000);

        Assert.Equal(64.00, panned.Zoom);
        Assert.True(double.IsFinite(panned.OffsetX));
        Assert.True(double.IsFinite(panned.OffsetY));

        // Coordinate conversion remains stable
        var rawPoint = new ImagePoint(540, 7500);
        ViewportPoint vp = panned.ImageToViewport(rawPoint);
        ImagePoint restored = panned.ViewportToImage(vp);
        Assert.Equal(rawPoint.X, restored.X, precision: 6);
        Assert.Equal(rawPoint.Y, restored.Y, precision: 6);

        // Fit always restores a valid recoverable view
        ViewportTransform recovered = ViewportTransform.Fit(1080, 15000, 1200, 800);
        Assert.True(recovered.Zoom < 0.10);
        Assert.True(double.IsFinite(recovered.OffsetX));
        Assert.True(double.IsFinite(recovered.OffsetY));
    }

    [Fact]
    public void PreviewToImage_ShouldAccountForDecodedPreviewScale()
    {
        ViewportTransform transform = ViewportTransform.Fit(1080, 15000, 1000, 700);

        ImagePoint raw = transform.PreviewToImage(new ImagePoint(270, 3750), 540, 7500);

        Assert.Equal(new ImagePoint(540, 7500), raw);
    }
}
