using Inklume.Domain.TextRegions;
using Xunit;

namespace Inklume.Domain.Tests;

public sealed class OcrRecognitionTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0.0, null)]
    [InlineData(0.5, 0.75)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.92, 0.0)]
    public void OcrRecognition_ShouldAcceptValidConfidences(double recConf, double? detConf)
    {
        var id = Guid.NewGuid();
        var regionId = Guid.NewGuid();
        const string text = "안녕하세요! Hello 123";

        var recognition = new OcrRecognition(
            id, regionId, text, recConf, detConf,
            "PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English",
            Timestamp, Timestamp);

        Assert.Equal(id, recognition.Id);
        Assert.Equal(regionId, recognition.TextRegionId);
        Assert.Equal(text, recognition.Text);
        Assert.Equal(recConf, recognition.RecognitionConfidence);
        Assert.Equal(detConf, recognition.DetectionConfidence);
        Assert.Equal("PaddleOCR", recognition.EngineName);
        Assert.Equal("3.7.0", recognition.EngineVersion);
        Assert.Equal("PP-OCRv5_mobile_det", recognition.DetectionModel);
        Assert.Equal("korean_PP-OCRv5_mobile_rec", recognition.RecognitionModel);
        Assert.Equal("Korean + English", recognition.ModelProfile);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void OcrRecognition_ShouldRejectInvalidRecognitionConfidence(double invalidConf)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OcrRecognition(
            Guid.NewGuid(), Guid.NewGuid(), "Sample", invalidConf, 0.5,
            "PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English",
            Timestamp, Timestamp));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void OcrRecognition_ShouldRejectInvalidDetectionConfidence(double invalidConf)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OcrRecognition(
            Guid.NewGuid(), Guid.NewGuid(), "Sample", 0.9, invalidConf,
            "PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English",
            Timestamp, Timestamp));
    }

    [Fact]
    public void OcrRecognition_ShouldPreserveKoreanUnicodeAndWhitespace()
    {
        const string koreanText = "  그것은... 정말 놀라운 일이었다!\n두 번째 줄   ";
        var recognition = new OcrRecognition(
            Guid.NewGuid(), Guid.NewGuid(), koreanText, 0.88, 0.95,
            "PaddleOCR", "3.7.0", "PP-OCRv5_mobile_det", "korean_PP-OCRv5_mobile_rec", "Korean + English",
            Timestamp, Timestamp);

        Assert.Equal(koreanText, recognition.Text);
    }

    [Fact]
    public void TextRegion_ShouldSupportOriginAndPreserveInOperations()
    {
        var geometry = new RectangleTextRegionGeometry(10, 20, 30, 40);
        var region = new TextRegion(
            Guid.NewGuid(), Guid.NewGuid(), geometry, 1,
            TextRegionRole.Unknown, TextContainerType.Unknown, Timestamp, Timestamp,
            TextRegionOrigin.OcrDetection);

        Assert.Equal(TextRegionOrigin.OcrDetection, region.Origin);

        var updated = region.WithClassification(TextRegionRole.Dialogue, TextContainerType.SpeechBubble, Timestamp.AddMinutes(1));
        Assert.Equal(TextRegionOrigin.OcrDetection, updated.Origin);
        Assert.Equal(TextRegionRole.Dialogue, updated.Role);

        var manualCopy = region.WithOrigin(TextRegionOrigin.Manual, Timestamp.AddMinutes(2));
        Assert.Equal(TextRegionOrigin.Manual, manualCopy.Origin);
    }
}
