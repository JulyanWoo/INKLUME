using Inklume.Domain.Projects;
using Xunit;

namespace Inklume.Domain.Tests;

public sealed class ChapterTests
{
    [Theory]
    [InlineData("1", "001")]
    [InlineData("10.5", "010.5")]
    [InlineData("125", "125")]
    [InlineData("1000", "1000")]
    public void ChapterNumber_ShouldCreateStableFolderName(string value, string expected)
    {
        ChapterNumber number = ChapterNumber.Parse(value);

        Assert.Equal(expected, number.ToFolderName());
    }

    [Fact]
    public void ChapterNumber_ShouldNormalizeEquivalentDecimalValues()
    {
        var first = new ChapterNumber(10.5m);
        var second = new ChapterNumber(10.50m);

        Assert.Equal(first, second);
        Assert.Equal("10.5", second.ToString());
    }

    [Fact]
    public void ChapterNumber_ShouldRejectNegativeValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChapterNumber(-0.5m));
    }

    [Fact]
    public void TryParse_ShouldNotInterpretThousandsSeparatorsAsDecimals()
    {
        Assert.False(ChapterNumber.TryParse("10,5", out _));
    }

    [Fact]
    public void Chapter_ShouldNormalizeOptionalTitleAndTimestamps()
    {
        var localTime = new DateTimeOffset(2026, 9, 13, 14, 0, 0, TimeSpan.FromHours(-5));

        var chapter = new Chapter(
            Guid.NewGuid(), Guid.NewGuid(), new ChapterNumber(12), "  Arrival  ", localTime, localTime.AddMinutes(2));

        Assert.Equal("Arrival", chapter.Title);
        Assert.Equal(TimeSpan.Zero, chapter.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, chapter.UpdatedAt.Offset);
    }

    [Fact]
    public void Chapter_ShouldRejectEmptyProjectIdentifier()
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() =>
            new Chapter(Guid.NewGuid(), Guid.Empty, new ChapterNumber(1), null, timestamp, timestamp));
    }
}
