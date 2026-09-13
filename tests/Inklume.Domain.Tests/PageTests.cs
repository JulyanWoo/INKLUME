using Inklume.Domain.Projects;
using Xunit;

namespace Inklume.Domain.Tests;

public sealed class PageTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Constructor_ShouldPreserveMetadataAndNormalizeHash()
    {
        var page = new Page(Guid.NewGuid(), Guid.NewGuid(), 3, "page 日本語.png", @"chapters\001\001_raw\003.png", Hash);

        Assert.Equal(3, page.Number);
        Assert.Equal("page 日本語.png", page.OriginalFileName);
        Assert.Equal(Hash.ToUpperInvariant(), page.ContentHash);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ShouldRejectNonPositivePageNumber(int number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Page(Guid.NewGuid(), Guid.NewGuid(), number, "page.png", "page.png", Hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void Constructor_ShouldRejectInvalidContentHash(string hash)
    {
        Assert.Throws<ArgumentException>(() =>
            new Page(Guid.NewGuid(), Guid.NewGuid(), 1, "page.png", "page.png", hash));
    }
}
