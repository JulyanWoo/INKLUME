using Inklume.Application.Projects;
using Inklume.Domain.Projects;
using Xunit;

namespace Inklume.Application.Tests;

public sealed class ProjectServiceTests
{
    private static readonly DateTimeOffset CurrentTime = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_ShouldPersistNewIdentityAndMetadata_WhenRequestIsValid()
    {
        var store = new RecordingProjectStore();
        var service = new ProjectService(store, new FixedTimeProvider(CurrentTime));
        var request = new CreateProjectRequest("  Moonlight Edition  ", "  Moonlight  ", "selected project folder");

        ProjectWorkspace workspace = await service.CreateAsync(request, TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, workspace.Project.Id);
        Assert.Equal("Moonlight Edition", workspace.Project.Name);
        Assert.Equal("Moonlight", workspace.Project.SeriesName);
        Assert.Equal(CurrentTime, workspace.Project.CreatedAt);
        Assert.Equal(CurrentTime, workspace.Project.UpdatedAt);
        Assert.Equal(request.RootPath, workspace.RootPath);
        Assert.Same(store.CreatedProject, workspace.Project);
        Assert.Equal(1, store.CreateCallCount);
    }

    [Fact]
    public async Task CreateAsync_ShouldGenerateDistinctIdentifiers_WhenCreatingSeparateProjects()
    {
        var service = new ProjectService(new RecordingProjectStore(), new FixedTimeProvider(CurrentTime));
        var request = new CreateProjectRequest("Project", "Series", "selected folder");

        ProjectWorkspace first = await service.CreateAsync(request, TestContext.Current.CancellationToken);
        ProjectWorkspace second = await service.CreateAsync(request, TestContext.Current.CancellationToken);

        Assert.NotEqual(first.Project.Id, second.Project.Id);
    }

    [Fact]
    public async Task CreateAsync_ShouldForwardCancellationToken_WhenCallingStore()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new RecordingProjectStore();
        var service = new ProjectService(store, new FixedTimeProvider(CurrentTime));
        var request = new CreateProjectRequest("Project", "Series", "selected folder");

        await service.CreateAsync(request, cancellation.Token);

        Assert.Equal(cancellation.Token, store.ReceivedCancellationToken);
    }

    [Fact]
    public async Task CreateAsync_ShouldNotCallStore_WhenCancellationWasRequested()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new RecordingProjectStore();
        var service = new ProjectService(store, new FixedTimeProvider(CurrentTime));
        var request = new CreateProjectRequest("Project", "Series", "selected folder");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(request, cancellation.Token));

        Assert.Equal(0, store.CreateCallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_ShouldRejectMissingPath_WhenNoFolderWasSelected(string rootPath)
    {
        var store = new RecordingProjectStore();
        var service = new ProjectService(store, new FixedTimeProvider(CurrentTime));
        var request = new CreateProjectRequest("Project", "Series", rootPath);

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            service.CreateAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidPath, exception.Code);
        Assert.Equal(0, store.CreateCallCount);
    }

    [Fact]
    public async Task CreateAsync_ShouldNotCallStore_WhenMetadataIsInvalid()
    {
        var store = new RecordingProjectStore();
        var service = new ProjectService(store, new FixedTimeProvider(CurrentTime));
        var request = new CreateProjectRequest(" ", "Series", "selected folder");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(0, store.CreateCallCount);
    }

    [Fact]
    public async Task OpenAsync_ShouldReturnStoredWorkspace_WhenProjectExists()
    {
        var existingProject = new TranslationProject(Guid.NewGuid(), "Edition", "Series", CurrentTime, CurrentTime);
        var existingWorkspace = new ProjectWorkspace(existingProject, "normalized project folder");
        var store = new RecordingProjectStore(existingWorkspace);
        var service = new ProjectService(store, new FixedTimeProvider(CurrentTime.AddDays(1)));

        ProjectWorkspace workspace = await service.OpenAsync("selected project folder", TestContext.Current.CancellationToken);

        Assert.Same(existingWorkspace, workspace);
        Assert.Equal("selected project folder", store.RequestedRootPath);
        Assert.Equal(CurrentTime, workspace.Project.UpdatedAt);
        Assert.Equal(1, store.OpenCallCount);
        Assert.Equal(0, store.CreateCallCount);
    }

    [Fact]
    public async Task OpenAsync_ShouldForwardCancellationToken_WhenCallingStore()
    {
        using var cancellation = new CancellationTokenSource();
        var project = new TranslationProject(Guid.NewGuid(), "Project", "Series", CurrentTime, CurrentTime);
        var store = new RecordingProjectStore(new ProjectWorkspace(project, "selected folder"));
        var service = new ProjectService(store, new FixedTimeProvider(CurrentTime));

        await service.OpenAsync("selected folder", cancellation.Token);

        Assert.Equal(cancellation.Token, store.ReceivedCancellationToken);
    }

    [Fact]
    public async Task OpenAsync_ShouldNotCallStore_WhenCancellationWasRequested()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new RecordingProjectStore();
        var service = new ProjectService(store, new FixedTimeProvider(CurrentTime));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.OpenAsync("selected folder", cancellation.Token));

        Assert.Equal(0, store.OpenCallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OpenAsync_ShouldRejectMissingPath_WhenNoFolderWasSelected(string rootPath)
    {
        var store = new RecordingProjectStore();
        var service = new ProjectService(store, new FixedTimeProvider(CurrentTime));

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            service.OpenAsync(rootPath, TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.InvalidPath, exception.Code);
        Assert.Equal(0, store.OpenCallCount);
    }

    [Fact]
    public async Task OpenAsync_ShouldPreserveStorageError_WhenStoreRejectsProject()
    {
        var originalException = new InvalidOperationException("The project format is unsupported.");
        var failure = new ProjectOperationException(ProjectErrorCode.IncompatibleVersion, "Upgrade INKLUME to open this project.", originalException);
        var store = new RecordingProjectStore { Failure = failure };
        var service = new ProjectService(store, new FixedTimeProvider(CurrentTime));

        ProjectOperationException exception = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            service.OpenAsync("selected folder", TestContext.Current.CancellationToken));

        Assert.Same(failure, exception);
        Assert.Same(originalException, exception.InnerException);
        Assert.Equal(ProjectErrorCode.IncompatibleVersion, exception.Code);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingProjectStore(ProjectWorkspace? existingWorkspace = null) : IProjectStore
    {
        public TranslationProject? CreatedProject { get; private set; }

        public string? RequestedRootPath { get; private set; }

        public CancellationToken ReceivedCancellationToken { get; private set; }

        public int CreateCallCount { get; private set; }

        public int OpenCallCount { get; private set; }

        public ProjectOperationException? Failure { get; init; }

        public Task<ProjectWorkspace> CreateAsync(
            TranslationProject project,
            string rootPath,
            CancellationToken cancellationToken)
        {
            CreateCallCount++;
            CreatedProject = project;
            RequestedRootPath = rootPath;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(new ProjectWorkspace(project, rootPath));
        }

        public Task<ProjectWorkspace> OpenAsync(string rootPath, CancellationToken cancellationToken)
        {
            OpenCallCount++;
            RequestedRootPath = rootPath;
            ReceivedCancellationToken = cancellationToken;

            if (Failure is not null)
            {
                return Task.FromException<ProjectWorkspace>(Failure);
            }

            return Task.FromResult(existingWorkspace ?? throw new InvalidOperationException("No test workspace was supplied."));
        }
    }
}
