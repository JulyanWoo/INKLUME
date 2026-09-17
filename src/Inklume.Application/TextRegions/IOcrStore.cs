using Inklume.Application.Projects;
using Inklume.Domain.TextRegions;

namespace Inklume.Application.TextRegions;

public interface IOcrStore
{
    Task<IReadOnlyDictionary<Guid, OcrRecognition>> GetRecognitionsForPageAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken);

    Task<OcrRecognition?> GetRecognitionForRegionAsync(
        ProjectWorkspace workspace,
        Guid regionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TextRegion>> ReplacePageOcrRegionsAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        IReadOnlyList<OcrDetectedRegion> detectedRegions,
        OcrEngineMetadata engineMetadata,
        CancellationToken cancellationToken);

    Task<OcrRecognition> SaveRegionRecognitionAsync(
        ProjectWorkspace workspace,
        Guid regionId,
        OcrRegionResult regionResult,
        CancellationToken cancellationToken);
}
