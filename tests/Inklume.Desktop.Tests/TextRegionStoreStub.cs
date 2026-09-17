using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;

namespace Inklume.Desktop.Tests;

internal sealed class TextRegionStoreStub : ITextRegionStore
{
    private readonly Dictionary<Guid, Page> _pages = [];
    private readonly List<TextRegion> _regions = [];

    public void AddPage(Page page) => _pages[page.Id] = page;

    public Task<Page?> GetPageAsync(ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
        => Task.FromResult(_pages.GetValueOrDefault(pageId));

    public Task<IReadOnlyList<TextRegion>> GetForPageAsync(
        ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TextRegion>>(
            [.. _regions.Where(region => region.PageId == pageId).OrderBy(region => region.ReadingOrder)]);

    public Task<int> GetNextReadingOrderAsync(
        ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
        => Task.FromResult(_regions.Where(region => region.PageId == pageId)
            .Select(region => region.ReadingOrder).DefaultIfEmpty().Max() + 1);

    public Task AddAsync(ProjectWorkspace workspace, TextRegion region, CancellationToken cancellationToken)
    {
        _regions.Add(region);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ProjectWorkspace workspace, TextRegion region, CancellationToken cancellationToken)
    {
        int index = _regions.FindIndex(candidate => candidate.Id == region.Id);
        if (index >= 0)
        {
            _regions[index] = region;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(ProjectWorkspace workspace, Guid regionId, CancellationToken cancellationToken)
    {
        _regions.RemoveAll(region => region.Id == regionId);
        return Task.CompletedTask;
    }

    public Task ReorderPageRegionsAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        IReadOnlyList<Guid> regionIdsInOrder,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < regionIdsInOrder.Count; i++)
        {
            Guid id = regionIdsInOrder[i];
            int index = _regions.FindIndex(item => item.Id == id && item.PageId == pageId);
            if (index >= 0)
            {
                _regions[index] = _regions[index].WithReadingOrder(i + 1, updatedAt);
            }
        }

        return Task.CompletedTask;
    }
}
