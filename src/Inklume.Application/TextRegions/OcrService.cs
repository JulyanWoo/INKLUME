using Inklume.Application.Projects;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;

namespace Inklume.Application.TextRegions;

public sealed class OcrService(
    IOcrProvider provider,
    IOcrStore ocrStore,
    ITextRegionStore regionStore)
{
    public async Task<IReadOnlyList<TextRegion>> AnalyzePageAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (pageId == Guid.Empty)
        {
            throw new ArgumentException("The page identifier must not be empty.", nameof(pageId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        Page page = await RequirePageAsync(workspace, pageId, cancellationToken);
        string imagePath = ResolveSourceImagePath(workspace, page);

        OcrPageResult pageResult;
        try
        {
            pageResult = await provider.AnalyzePageAsync(imagePath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProjectOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProjectOperationException(
                ProjectErrorCode.OcrProviderFailure,
                "OCR provider execution failed.",
                exception);
        }

        cancellationToken.ThrowIfCancellationRequested();

        int pixelWidth = page.PixelWidth.GetValueOrDefault();
        int pixelHeight = page.PixelHeight.GetValueOrDefault();

        foreach (OcrDetectedRegion detected in pageResult.Regions)
        {
            if (detected.Geometry is null || !detected.Geometry.IsWithin(pixelWidth, pixelHeight))
            {
                throw new ProjectOperationException(
                    ProjectErrorCode.OcrInvalidResult,
                    "OCR detection produced geometry outside RAW image bounds.");
            }
        }

        return await ocrStore.ReplacePageOcrRegionsAsync(
            workspace,
            pageId,
            pageResult.Regions,
            pageResult.EngineInfo,
            cancellationToken);
    }

    public async Task<OcrRecognition> RecognizeRegionAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        Guid regionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (pageId == Guid.Empty)
        {
            throw new ArgumentException("The page identifier must not be empty.", nameof(pageId));
        }

        if (regionId == Guid.Empty)
        {
            throw new ArgumentException("The region identifier must not be empty.", nameof(regionId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        Page page = await RequirePageAsync(workspace, pageId, cancellationToken);
        string imagePath = ResolveSourceImagePath(workspace, page);

        IReadOnlyList<TextRegion> regions = await regionStore.GetForPageAsync(workspace, pageId, cancellationToken);
        TextRegion region = regions.FirstOrDefault(candidate => candidate.Id == regionId)
            ?? throw new ProjectOperationException(
                ProjectErrorCode.InvalidRegion,
                "The specified text region does not exist on this page.");

        OcrRegionResult regionResult;
        try
        {
            regionResult = await provider.RecognizeRegionAsync(imagePath, region.Geometry, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProjectOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProjectOperationException(
                ProjectErrorCode.OcrProviderFailure,
                "OCR region recognition failed.",
                exception);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return await ocrStore.SaveRegionRecognitionAsync(workspace, regionId, regionResult, cancellationToken);
    }

    public Task<IReadOnlyDictionary<Guid, OcrRecognition>> GetRecognitionsForPageAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (pageId == Guid.Empty)
        {
            throw new ArgumentException("The page identifier must not be empty.", nameof(pageId));
        }

        return ocrStore.GetRecognitionsForPageAsync(workspace, pageId, cancellationToken);
    }

    public Task<OcrRecognition?> GetRecognitionForRegionAsync(
        ProjectWorkspace workspace,
        Guid regionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (regionId == Guid.Empty)
        {
            throw new ArgumentException("The region identifier must not be empty.", nameof(regionId));
        }

        return ocrStore.GetRecognitionForRegionAsync(workspace, regionId, cancellationToken);
    }

    private async Task<Page> RequirePageAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        Page? page = await regionStore.GetPageAsync(workspace, pageId, cancellationToken);
        if (page is null)
        {
            throw new ProjectOperationException(
                ProjectErrorCode.InvalidRegion,
                "The selected page does not belong to the open project.");
        }

        if (!page.HasDimensions)
        {
            throw new ProjectOperationException(
                ProjectErrorCode.OcrDimensionsUnavailable,
                "The RAW page dimensions must be resolved before executing OCR.");
        }

        return page;
    }

    private static string ResolveSourceImagePath(ProjectWorkspace workspace, Page page)
    {
        string fullPath = Path.GetFullPath(Path.Combine(workspace.SourceRoot, page.RelativePath));
        string allowedPrefix = string.Concat(Path.GetFullPath(workspace.SourceRoot), Path.DirectorySeparatorChar);

        if (!fullPath.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            throw new ProjectOperationException(
                ProjectErrorCode.OcrSourceMissing,
                "The canonical source image file could not be found on disk.");
        }

        return fullPath;
    }
}
