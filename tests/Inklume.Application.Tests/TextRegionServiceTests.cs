using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;
using Xunit;

namespace Inklume.Application.Tests;

public sealed class TextRegionServiceTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_ShouldAppendOneBasedReadingOrder()
    {
        (TextRegionService service, InMemoryTextRegionStore store, ProjectWorkspace workspace, Page page) = CreateFixture();

        TextRegion first = await service.CreateAsync(
            workspace, page.Id, new RectangleTextRegionGeometry(10, 20, 30, 40),
            cancellationToken: TestContext.Current.CancellationToken);
        TextRegion second = await service.CreateAsync(
            workspace, page.Id,
            new PolygonTextRegionGeometry(
                [new ImagePoint(100, 100), new ImagePoint(200, 100), new ImagePoint(150, 200)]),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, first.ReadingOrder);
        Assert.Equal(2, second.ReadingOrder);
        Assert.Equal(TextRegionRole.Unknown, first.Role);
        Assert.Equal([first, second], store.Regions);
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectMissingDimensionsAndOutsideGeometry()
    {
        (TextRegionService service, InMemoryTextRegionStore store, ProjectWorkspace workspace, Page page) = CreateFixture();
        var unresolved = new Page(
            Guid.NewGuid(), page.ChapterId, 2, "old.png", "old.png", new string('B', 64));
        store.Pages[unresolved.Id] = unresolved;

        ProjectOperationException missingDimensions = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            service.CreateAsync(workspace, unresolved.Id, new RectangleTextRegionGeometry(1, 1, 10, 10),
                cancellationToken: TestContext.Current.CancellationToken));
        ProjectOperationException outsideBounds = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            service.CreateAsync(workspace, page.Id, new RectangleTextRegionGeometry(1070, 20, 30, 40),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidRegion, missingDimensions.Code);
        Assert.Equal(ProjectErrorCode.InvalidRegion, outsideBounds.Code);
        Assert.Empty(store.Regions);
    }

    [Fact]
    public async Task GetForPageAsync_ShouldKeepRegionsIsolatedByPage()
    {
        (TextRegionService service, InMemoryTextRegionStore store, ProjectWorkspace workspace, Page page) = CreateFixture();
        var secondPage = new Page(
            Guid.NewGuid(), page.ChapterId, 2, "second.png", "second.png", new string('C', 64), 1080, 15000);
        store.Pages[secondPage.Id] = secondPage;
        TextRegion firstPageRegion = await service.CreateAsync(
            workspace, page.Id, new RectangleTextRegionGeometry(10, 10, 50, 50),
            cancellationToken: TestContext.Current.CancellationToken);
        await service.CreateAsync(
            workspace, secondPage.Id, new RectangleTextRegionGeometry(20, 20, 50, 50),
            cancellationToken: TestContext.Current.CancellationToken);

        IReadOnlyList<TextRegion> regions = await service.GetForPageAsync(
            workspace, page.Id, TestContext.Current.CancellationToken);

        Assert.Equal(firstPageRegion, Assert.Single(regions));
    }

    [Fact]
    public async Task UpdateAndDelete_ShouldPersistClassificationAndRemoveRegion()
    {
        (TextRegionService service, InMemoryTextRegionStore store, ProjectWorkspace workspace, Page page) = CreateFixture();
        TextRegion region = await service.CreateAsync(
            workspace, page.Id, new RectangleTextRegionGeometry(10, 10, 50, 50),
            cancellationToken: TestContext.Current.CancellationToken);

        TextRegion updated = await service.UpdateClassificationAsync(
            workspace, region, TextRegionRole.Sfx, TextContainerType.None,
            TestContext.Current.CancellationToken);
        await service.DeleteAsync(workspace, updated, TestContext.Current.CancellationToken);

        Assert.Equal(TextRegionRole.Sfx, updated.Role);
        Assert.Equal(TextContainerType.None, updated.ContainerType);
        Assert.Empty(store.Regions);
    }

    private static (TextRegionService, InMemoryTextRegionStore, ProjectWorkspace, Page) CreateFixture()
    {
        var project = new TranslationProject(Guid.NewGuid(), "Project", "Series", Timestamp, Timestamp);
        var workspace = new ProjectWorkspace(project, @"C:\Project", @"C:\Data\Project");
        var page = new Page(
            Guid.NewGuid(), Guid.NewGuid(), 1, "page.png", "page.png", new string('A', 64), 1080, 15000);
        var store = new InMemoryTextRegionStore();
        store.Pages[page.Id] = page;
        return (new TextRegionService(store, new FixedTimeProvider()), store, workspace, page);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Timestamp;
    }

    private sealed class InMemoryTextRegionStore : ITextRegionStore
    {
        public Dictionary<Guid, Page> Pages { get; } = [];

        public List<TextRegion> Regions { get; } = [];

        public Task<Page?> GetPageAsync(ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult(Pages.GetValueOrDefault(pageId));

        public Task<IReadOnlyList<TextRegion>> GetForPageAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TextRegion>>(
                [.. Regions.Where(region => region.PageId == pageId).OrderBy(region => region.ReadingOrder)]);

        public Task<int> GetNextReadingOrderAsync(
            ProjectWorkspace workspace, Guid pageId, CancellationToken cancellationToken)
            => Task.FromResult(Regions.Where(region => region.PageId == pageId)
                .Select(region => region.ReadingOrder).DefaultIfEmpty().Max() + 1);

        public Task AddAsync(ProjectWorkspace workspace, TextRegion region, CancellationToken cancellationToken)
        {
            Regions.Add(region);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ProjectWorkspace workspace, TextRegion region, CancellationToken cancellationToken)
        {
            int index = Regions.FindIndex(item => item.Id == region.Id);
            Regions[index] = region;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(ProjectWorkspace workspace, Guid regionId, CancellationToken cancellationToken)
        {
            Regions.RemoveAll(region => region.Id == regionId);
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
                int index = Regions.FindIndex(item => item.Id == id && item.PageId == pageId);
                if (index >= 0)
                {
                    Regions[index] = Regions[index].WithReadingOrder(i + 1, updatedAt);
                }
            }

            return Task.CompletedTask;
        }
    }
}
