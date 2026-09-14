using Inklume.Application.Projects;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;

namespace Inklume.Application.TextRegions;

public sealed class TextRegionService(ITextRegionStore store, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<TextRegion>> GetForPageAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        await RequirePageAsync(workspace, pageId, cancellationToken);
        return await store.GetForPageAsync(workspace, pageId, cancellationToken);
    }

    public async Task<TextRegion> CreateAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        TextRegionGeometry geometry,
        TextRegionRole role = TextRegionRole.Unknown,
        TextContainerType containerType = TextContainerType.Unknown,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        Page page = await RequirePageAsync(workspace, pageId, cancellationToken);
        ValidateGeometry(page, geometry);
        int readingOrder = await store.GetNextReadingOrderAsync(workspace, pageId, cancellationToken);
        DateTimeOffset timestamp = timeProvider.GetUtcNow();
        var region = new TextRegion(
            Guid.NewGuid(), pageId, geometry, readingOrder, role, containerType, timestamp, timestamp);
        await store.AddAsync(workspace, region, cancellationToken);
        return region;
    }

    public async Task<TextRegion> UpdateClassificationAsync(
        ProjectWorkspace workspace,
        TextRegion region,
        TextRegionRole role,
        TextContainerType containerType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(region);
        cancellationToken.ThrowIfCancellationRequested();
        TextRegion updated = region.WithClassification(role, containerType, timeProvider.GetUtcNow());
        await store.UpdateAsync(workspace, updated, cancellationToken);
        return updated;
    }

    public async Task DeleteAsync(
        ProjectWorkspace workspace,
        TextRegion region,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(region);
        cancellationToken.ThrowIfCancellationRequested();
        await store.DeleteAsync(workspace, region.Id, cancellationToken);
    }

    private async Task<Page> RequirePageAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (pageId == Guid.Empty)
        {
            throw new ArgumentException("The page identifier must not be empty.", nameof(pageId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Page? page = await store.GetPageAsync(workspace, pageId, cancellationToken);
        return page ?? throw new ProjectOperationException(
            ProjectErrorCode.InvalidRegion, "The selected page does not belong to the open project.");
    }

    private static void ValidateGeometry(Page page, TextRegionGeometry geometry)
    {
        if (!page.HasDimensions)
        {
            throw new ProjectOperationException(
                ProjectErrorCode.InvalidRegion,
                "The RAW page dimensions must be resolved before creating text regions.");
        }

        int pixelWidth = page.PixelWidth.GetValueOrDefault();
        int pixelHeight = page.PixelHeight.GetValueOrDefault();
        if (!geometry.IsWithin(pixelWidth, pixelHeight))
        {
            throw new ProjectOperationException(
                ProjectErrorCode.InvalidRegion, "The text region must remain inside the RAW image bounds.");
        }
    }
}
