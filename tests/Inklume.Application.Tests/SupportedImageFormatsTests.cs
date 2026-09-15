using Inklume.Application.Chapters;
using Xunit;

namespace Inklume.Application.Tests;

public sealed class SupportedImageFormatsTests
{
    [Theory]
    [InlineData(".png")]
    [InlineData(".PNG")]
    [InlineData(".jpg")]
    [InlineData(".JPG")]
    [InlineData(".jpeg")]
    [InlineData(".JPEG")]
    [InlineData(".webp")]
    [InlineData(".WEBP")]
    [InlineData("image.png")]
    [InlineData(@"C:\path\to\file.JPG")]
    [InlineData("folder/scan.webp")]
    public void IsSupported_ShouldReturnTrue_ForSupportedExtensionsAndPaths(string pathOrExtension)
    {
        Assert.True(SupportedImageFormats.IsSupported(pathOrExtension));
    }

    [Theory]
    [InlineData(".gif")]
    [InlineData(".bmp")]
    [InlineData(".tiff")]
    [InlineData(".txt")]
    [InlineData(".pdf")]
    [InlineData(".zip")]
    [InlineData("noextension")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSupported_ShouldReturnFalse_ForUnsupportedExtensionsAndPaths(string pathOrExtension)
    {
        Assert.False(SupportedImageFormats.IsSupported(pathOrExtension));
    }

    [Fact]
    public void Extensions_ShouldContainExpectedCanonicalFormats()
    {
        Assert.Contains(".png", SupportedImageFormats.Extensions);
        Assert.Contains(".jpg", SupportedImageFormats.Extensions);
        Assert.Contains(".jpeg", SupportedImageFormats.Extensions);
        Assert.Contains(".webp", SupportedImageFormats.Extensions);
        Assert.Equal(4, SupportedImageFormats.Extensions.Count);
    }
}
