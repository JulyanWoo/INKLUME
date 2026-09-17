using Inklume.Desktop.VisualEditor;
using Xunit;

namespace Inklume.Desktop.Tests;

public sealed class ZoomLevelsTests
{
    [Fact]
    public void ZoomLevels_HasExpectedDiscreteLevelsCountAndValues()
    {
        Assert.Equal(22, ZoomLevels.Levels.Length);
        Assert.Equal(0.10, ZoomLevels.MinimumManualZoom);
        Assert.Equal(64.00, ZoomLevels.MaximumManualZoom);
        Assert.Equal(1.00, ZoomLevels.DefaultZoom);
        Assert.Equal(0.10, ZoomLevels.Levels[0]);
        Assert.Equal(64.00, ZoomLevels.Levels[^1]);
    }

    [Theory]
    [InlineData(0.10, 0.125)]
    [InlineData(0.125, 0.167)]
    [InlineData(0.167, 0.25)]
    [InlineData(0.25, 0.333)]
    [InlineData(0.333, 0.50)]
    [InlineData(0.50, 0.667)]
    [InlineData(0.667, 0.75)]
    [InlineData(0.75, 1.00)]
    [InlineData(1.00, 1.25)]
    [InlineData(1.25, 1.50)]
    [InlineData(1.50, 2.00)]
    [InlineData(2.00, 3.00)]
    [InlineData(3.00, 4.00)]
    [InlineData(4.00, 6.00)]
    [InlineData(6.00, 8.00)]
    [InlineData(8.00, 12.00)]
    [InlineData(12.00, 16.00)]
    [InlineData(16.00, 24.00)]
    [InlineData(24.00, 32.00)]
    [InlineData(32.00, 48.00)]
    [InlineData(48.00, 64.00)]
    public void GetNextZoomIn_MovesToNextDiscreteLevel(double current, double expectedNext)
    {
        double actualNext = ZoomLevels.GetNextZoomIn(current);
        Assert.Equal(expectedNext, actualNext);
    }

    [Theory]
    [InlineData(64.00, 48.00)]
    [InlineData(48.00, 32.00)]
    [InlineData(32.00, 24.00)]
    [InlineData(24.00, 16.00)]
    [InlineData(16.00, 12.00)]
    [InlineData(12.00, 8.00)]
    [InlineData(8.00, 6.00)]
    [InlineData(6.00, 4.00)]
    [InlineData(4.00, 3.00)]
    [InlineData(3.00, 2.00)]
    [InlineData(2.00, 1.50)]
    [InlineData(1.50, 1.25)]
    [InlineData(1.25, 1.00)]
    [InlineData(1.00, 0.75)]
    [InlineData(0.75, 0.667)]
    [InlineData(0.667, 0.50)]
    [InlineData(0.50, 0.333)]
    [InlineData(0.333, 0.25)]
    [InlineData(0.25, 0.167)]
    [InlineData(0.167, 0.125)]
    [InlineData(0.125, 0.10)]
    public void GetNextZoomOut_MovesToPreviousDiscreteLevel(double current, double expectedPrevious)
    {
        double actualPrevious = ZoomLevels.GetNextZoomOut(current);
        Assert.Equal(expectedPrevious, actualPrevious);
    }

    [Fact]
    public void GetNextZoomIn_AtMaximum_CannotExceed6400Percent()
    {
        Assert.Equal(64.00, ZoomLevels.GetNextZoomIn(64.00));
        Assert.Equal(64.00, ZoomLevels.GetNextZoomIn(70.00));
    }

    [Fact]
    public void GetNextZoomOut_AtMinimum_CannotGoBelow10Percent()
    {
        Assert.Equal(0.10, ZoomLevels.GetNextZoomOut(0.10));
    }

    [Fact]
    public void SteppingFromFitScaleBelowMinimum_AdvancesToMinimumManualZoom()
    {
        double fitScale = 0.024;

        double nextZoomIn = ZoomLevels.GetNextZoomIn(fitScale);
        Assert.Equal(0.10, nextZoomIn);

        double nextZoomOut = ZoomLevels.GetNextZoomOut(fitScale);
        Assert.Equal(0.10, nextZoomOut);
    }

    [Theory]
    [InlineData(0.42, 0.50, 0.333)]
    [InlineData(0.999, 1.00, 0.75)]
    [InlineData(1.001, 1.25, 1.00)]
    [InlineData(2.50, 3.00, 2.00)]
    [InlineData(5.00, 6.00, 4.00)]
    [InlineData(10.00, 12.00, 8.00)]
    public void SteppingFromArbitraryZoom_SelectsEnclosingDiscreteLevels(
        double arbitraryZoom,
        double expectedZoomIn,
        double expectedZoomOut)
    {
        Assert.Equal(expectedZoomIn, ZoomLevels.GetNextZoomIn(arbitraryZoom));
        Assert.Equal(expectedZoomOut, ZoomLevels.GetNextZoomOut(arbitraryZoom));
    }
}
