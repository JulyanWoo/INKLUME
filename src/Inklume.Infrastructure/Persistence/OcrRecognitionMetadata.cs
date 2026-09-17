namespace Inklume.Infrastructure.Persistence;

internal sealed class OcrRecognitionMetadata
{
    public Guid Id { get; set; }

    public Guid TextRegionId { get; set; }

    public string Text { get; set; } = string.Empty;

    public double RecognitionConfidence { get; set; }

    public double? DetectionConfidence { get; set; }

    public string EngineName { get; set; } = string.Empty;

    public string EngineVersion { get; set; } = string.Empty;

    public string DetectionModel { get; set; } = string.Empty;

    public string RecognitionModel { get; set; } = string.Empty;

    public string ModelProfile { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
