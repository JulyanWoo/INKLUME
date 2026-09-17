using Inklume.Application.Projects;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;

namespace Inklume.Application.TextRegions;

public sealed class TextRegionReviewService(
    ITextRegionStore regionStore,
    IOcrStore ocrStore,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<TextRegion> SaveReviewedTextAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        Guid regionId,
        string? reviewedText,
        CancellationToken cancellationToken = default)
    {
        TextRegion region = await RequireRegionAsync(workspace, pageId, regionId, cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        TextRegion updated = region.WithReview(
            reviewedText,
            region.ReviewStatus,
            region.ReviewedAt,
            userModifiedAt: now,
            updatedAt: now);

        await regionStore.UpdateAsync(workspace, updated, cancellationToken);
        return updated;
    }

    public async Task<TextRegion> ResetReviewedTextAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        Guid regionId,
        CancellationToken cancellationToken = default)
    {
        TextRegion region = await RequireRegionAsync(workspace, pageId, regionId, cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        TextRegion updated = region.WithReview(
            reviewedText: null,
            region.ReviewStatus,
            region.ReviewedAt,
            userModifiedAt: now,
            updatedAt: now);

        await regionStore.UpdateAsync(workspace, updated, cancellationToken);
        return updated;
    }

    public async Task<TextRegion> SetReviewStatusAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        Guid regionId,
        TextRegionReviewStatus status,
        CancellationToken cancellationToken = default)
    {
        TextRegion region = await RequireRegionAsync(workspace, pageId, regionId, cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset? reviewedAt = status == TextRegionReviewStatus.Reviewed ? now : null;

        TextRegion updated = region.WithReview(
            region.ReviewedText,
            status,
            reviewedAt,
            userModifiedAt: now,
            updatedAt: now);

        await regionStore.UpdateAsync(workspace, updated, cancellationToken);
        return updated;
    }

    public async Task<TextRegion> UpdateClassificationAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        Guid regionId,
        TextRegionRole role,
        TextContainerType containerType,
        CancellationToken cancellationToken = default)
    {
        TextRegion region = await RequireRegionAsync(workspace, pageId, regionId, cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        TextRegion updated = region.WithClassification(
            role,
            containerType,
            updatedAt: now,
            userModifiedAt: now);

        await regionStore.UpdateAsync(workspace, updated, cancellationToken);
        return updated;
    }

    public async Task<IReadOnlyList<TextRegion>> MoveRegionUpAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        Guid regionId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TextRegion> regions = await regionStore.GetForPageAsync(workspace, pageId, cancellationToken);
        int index = -1;
        for (int i = 0; i < regions.Count; i++)
        {
            if (regions[i].Id == regionId)
            {
                index = i;
                break;
            }
        }

        if (index <= 0)
        {
            return regions;
        }

        List<Guid> orderedIds = regions.Select(r => r.Id).ToList();
        (orderedIds[index - 1], orderedIds[index]) = (orderedIds[index], orderedIds[index - 1]);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        await regionStore.ReorderPageRegionsAsync(workspace, pageId, orderedIds, userModifiedAt: now, cancellationToken);
        return await regionStore.GetForPageAsync(workspace, pageId, cancellationToken);
    }

    public async Task<IReadOnlyList<TextRegion>> MoveRegionDownAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        Guid regionId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TextRegion> regions = await regionStore.GetForPageAsync(workspace, pageId, cancellationToken);
        int index = -1;
        for (int i = 0; i < regions.Count; i++)
        {
            if (regions[i].Id == regionId)
            {
                index = i;
                break;
            }
        }

        if (index < 0 || index >= regions.Count - 1)
        {
            return regions;
        }

        List<Guid> orderedIds = regions.Select(r => r.Id).ToList();
        (orderedIds[index], orderedIds[index + 1]) = (orderedIds[index + 1], orderedIds[index]);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        await regionStore.ReorderPageRegionsAsync(workspace, pageId, orderedIds, userModifiedAt: now, cancellationToken);
        return await regionStore.GetForPageAsync(workspace, pageId, cancellationToken);
    }

    public async Task<RegionSourceTextDto> GetEffectiveSourceTextAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        Guid regionId,
        CancellationToken cancellationToken = default)
    {
        TextRegion region = await RequireRegionAsync(workspace, pageId, regionId, cancellationToken);
        OcrRecognition? ocr = await ocrStore.GetRecognitionForRegionAsync(workspace, regionId, cancellationToken);

        return new RegionSourceTextDto(
            region.Id,
            ocr?.Text,
            region.ReviewedText,
            region.ReviewStatus,
            region.Role,
            region.ContainerType,
            region.ReadingOrder);
    }

    public async Task<(int Reviewed, int Total)> GetPageReviewProgressAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TextRegion> regions = await regionStore.GetForPageAsync(workspace, pageId, cancellationToken);
        int reviewed = regions.Count(r => r.ReviewStatus == TextRegionReviewStatus.Reviewed);
        return (reviewed, regions.Count);
    }

    private async Task<TextRegion> RequireRegionAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        Guid regionId,
        CancellationToken cancellationToken)
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

        IReadOnlyList<TextRegion> regions = await regionStore.GetForPageAsync(workspace, pageId, cancellationToken);
        return regions.FirstOrDefault(r => r.Id == regionId)
            ?? throw new ProjectOperationException(
                ProjectErrorCode.InvalidRegion,
                "The specified text region does not exist on this page.");
    }
}
