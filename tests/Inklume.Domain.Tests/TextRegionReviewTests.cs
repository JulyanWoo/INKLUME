using Inklume.Domain.TextRegions;
using Xunit;

namespace Inklume.Domain.Tests;

public sealed class TextRegionReviewTests
{
    private static readonly DateTimeOffset BaseCreated = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BaseUpdated = new(2026, 9, 17, 10, 5, 0, TimeSpan.Zero);
    private static readonly RectangleTextRegionGeometry Geometry = new(10, 20, 100, 50);

    [Fact]
    public void Constructor_DefaultsReviewProperties_ToPendingAndNull()
    {
        var region = new TextRegion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Geometry,
            1,
            TextRegionRole.Unknown,
            TextContainerType.Unknown,
            BaseCreated,
            BaseUpdated,
            TextRegionOrigin.OcrDetection);

        Assert.Equal(TextRegionReviewStatus.Pending, region.ReviewStatus);
        Assert.Null(region.ReviewedText);
        Assert.Null(region.ReviewedAt);
        Assert.Null(region.UserModifiedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("안녕하세요")]
    [InlineData("Hello World\nLine 2\twith   spacing")]
    public void Constructor_PreservesReviewedTextExactValue(string? reviewedText)
    {
        var region = new TextRegion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Geometry,
            1,
            TextRegionRole.Dialogue,
            TextContainerType.SpeechBubble,
            BaseCreated,
            BaseUpdated,
            TextRegionOrigin.Manual,
            reviewedText: reviewedText);

        Assert.Equal(reviewedText, region.ReviewedText);
    }

    [Fact]
    public void Constructor_RejectsInvalidReviewStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextRegion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Geometry,
            1,
            TextRegionRole.Unknown,
            TextContainerType.Unknown,
            BaseCreated,
            BaseUpdated,
            reviewStatus: (TextRegionReviewStatus)999));
    }

    [Fact]
    public void WithReview_UpdatesReviewedTextAndTimestamps()
    {
        var original = new TextRegion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Geometry,
            1,
            TextRegionRole.Unknown,
            TextContainerType.Unknown,
            BaseCreated,
            BaseUpdated,
            TextRegionOrigin.OcrDetection);

        DateTimeOffset reviewTime = new(2026, 9, 17, 11, 0, 0, TimeSpan.Zero);
        TextRegion reviewed = original.WithReview(
            "Reviewed Korean 안녕하세요",
            TextRegionReviewStatus.Reviewed,
            reviewTime,
            reviewTime,
            reviewTime);

        Assert.Equal(original.Id, reviewed.Id);
        Assert.Equal(original.PageId, reviewed.PageId);
        Assert.Equal(original.Geometry, reviewed.Geometry);
        Assert.Equal(original.ReadingOrder, reviewed.ReadingOrder);
        Assert.Equal(original.Origin, reviewed.Origin);
        Assert.Equal("Reviewed Korean 안녕하세요", reviewed.ReviewedText);
        Assert.Equal(TextRegionReviewStatus.Reviewed, reviewed.ReviewStatus);
        Assert.Equal(reviewTime.ToUniversalTime(), reviewed.ReviewedAt);
        Assert.Equal(reviewTime.ToUniversalTime(), reviewed.UserModifiedAt);
    }

    [Fact]
    public void WithReadingOrder_UpdatesOrderAndUserModifiedAt()
    {
        var original = new TextRegion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Geometry,
            1,
            TextRegionRole.Dialogue,
            TextContainerType.SpeechBubble,
            BaseCreated,
            BaseUpdated,
            TextRegionOrigin.Manual);

        DateTimeOffset modifyTime = new(2026, 9, 17, 11, 30, 0, TimeSpan.Zero);
        TextRegion reordered = original.WithReadingOrder(5, modifyTime, modifyTime);

        Assert.Equal(5, reordered.ReadingOrder);
        Assert.Equal(modifyTime.ToUniversalTime(), reordered.UserModifiedAt);
        Assert.Equal(original.Role, reordered.Role);
        Assert.Equal(original.ContainerType, reordered.ContainerType);
    }

    [Fact]
    public void WithClassification_UpdatesRoleAndUserModifiedAt()
    {
        var original = new TextRegion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Geometry,
            1,
            TextRegionRole.Unknown,
            TextContainerType.Unknown,
            BaseCreated,
            BaseUpdated,
            TextRegionOrigin.OcrDetection);

        DateTimeOffset modifyTime = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
        TextRegion classified = original.WithClassification(
            TextRegionRole.Dialogue,
            TextContainerType.SpeechBubble,
            modifyTime,
            modifyTime);

        Assert.Equal(TextRegionRole.Dialogue, classified.Role);
        Assert.Equal(TextContainerType.SpeechBubble, classified.ContainerType);
        Assert.Equal(modifyTime.ToUniversalTime(), classified.UserModifiedAt);
    }
}
