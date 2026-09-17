using Inklume.Domain.TextRegions;

namespace Inklume.Application.TextRegions;

public sealed record OcrEngineMetadata(
    string EngineName,
    string EngineVersion,
    string DetectionModel,
    string RecognitionModel,
    string ModelProfile);

public sealed record OcrDetectedRegion(
    PolygonTextRegionGeometry Geometry,
    string Text,
    double RecognitionConfidence,
    double? DetectionConfidence);

public sealed record OcrPageResult(
    IReadOnlyList<OcrDetectedRegion> Regions,
    OcrEngineMetadata EngineInfo);

public sealed record OcrRegionResult(
    string Text,
    double RecognitionConfidence,
    OcrEngineMetadata EngineInfo);

public interface IOcrProvider
{
    Task<OcrPageResult> AnalyzePageAsync(string imagePath, CancellationToken cancellationToken);

    Task<OcrRegionResult> RecognizeRegionAsync(string imagePath, TextRegionGeometry geometry, CancellationToken cancellationToken);
}
