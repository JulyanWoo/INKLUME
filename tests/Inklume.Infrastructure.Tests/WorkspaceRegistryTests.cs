using System.Text.Json;
using Inklume.Application.Workspaces;
using Inklume.Infrastructure.Workspaces;
using Xunit;

namespace Inklume.Infrastructure.Tests;

public sealed class WorkspaceRegistryTests : IDisposable
{
    private readonly TemporaryWorkspace _temporaryWorkspace = new();
    private readonly DefaultWorkspaceDataLocation _dataLocation;
    private readonly JsonWorkspaceRegistry _registry;

    public WorkspaceRegistryTests()
    {
        _dataLocation = new DefaultWorkspaceDataLocation(Path.Combine(_temporaryWorkspace.RootPath, "AppData"));
        _registry = new JsonWorkspaceRegistry(_dataLocation);
    }

    [Fact]
    public async Task RegisterOrUpdateAsync_ShouldPersistAndRetrieveEntry()
    {
        Guid projectId = Guid.NewGuid();
        string sourceRoot = _temporaryWorkspace.GetPath("MangaSeries");
        Directory.CreateDirectory(sourceRoot);
        string dataRoot = _dataLocation.GetDataRoot(projectId);
        Directory.CreateDirectory(dataRoot);

        var now = DateTimeOffset.UtcNow;
        var entry = new WorkspaceRegistryEntry(
            projectId,
            "Manga Project",
            "Manga Series",
            sourceRoot,
            dataRoot,
            now,
            now,
            now,
            IsLegacyMigrated: false);

        await _registry.RegisterOrUpdateAsync(entry, TestContext.Current.CancellationToken);

        WorkspaceRegistryEntry? bySource = await _registry.FindBySourceRootAsync(sourceRoot, TestContext.Current.CancellationToken);
        Assert.NotNull(bySource);
        Assert.Equal(projectId, bySource.ProjectId);
        Assert.Equal("Manga Project", bySource.ProjectName);
        Assert.Equal(Path.GetFullPath(sourceRoot), bySource.SourceRoot);
        Assert.Equal(Path.GetFullPath(dataRoot), bySource.DataRoot);

        WorkspaceRegistryEntry? byId = await _registry.FindByIdAsync(projectId, TestContext.Current.CancellationToken);
        Assert.NotNull(byId);
        Assert.Equal(projectId, byId.ProjectId);
    }

    [Fact]
    public async Task FindBySourceRootAsync_ShouldMatchRegardlessOfTrailingSeparatorsAndCasing()
    {
        Guid projectId = Guid.NewGuid();
        string sourceRoot = _temporaryWorkspace.GetPath("CaseSensitivityFolder");
        Directory.CreateDirectory(sourceRoot);
        string dataRoot = _dataLocation.GetDataRoot(projectId);
        Directory.CreateDirectory(dataRoot);

        var now = DateTimeOffset.UtcNow;
        var entry = new WorkspaceRegistryEntry(
            projectId,
            "Case Project",
            "Case Series",
            sourceRoot,
            dataRoot,
            now,
            now,
            now,
            IsLegacyMigrated: false);

        await _registry.RegisterOrUpdateAsync(entry, TestContext.Current.CancellationToken);

        // Test trailing slash
        WorkspaceRegistryEntry? withTrailing = await _registry.FindBySourceRootAsync(
            sourceRoot + Path.DirectorySeparatorChar, TestContext.Current.CancellationToken);
        Assert.NotNull(withTrailing);
        Assert.Equal(projectId, withTrailing.ProjectId);

        // Test forward slashes
        WorkspaceRegistryEntry? withForwardSlashes = await _registry.FindBySourceRootAsync(
            sourceRoot.Replace('\\', '/'), TestContext.Current.CancellationToken);
        Assert.NotNull(withForwardSlashes);
        Assert.Equal(projectId, withForwardSlashes.ProjectId);

        // Test different casing (Windows filesystem is case-insensitive)
        WorkspaceRegistryEntry? withLower = await _registry.FindBySourceRootAsync(
            sourceRoot.ToLowerInvariant(), TestContext.Current.CancellationToken);
        Assert.NotNull(withLower);
        Assert.Equal(projectId, withLower.ProjectId);
    }

    [Fact]
    public async Task CorruptedRegistry_ShouldPreserveBackupAndReconstructFromWorkspaces()
    {
        // 1. Create two workspaces with valid workspace.json descriptors
        Guid project1Id = Guid.NewGuid();
        string source1 = _temporaryWorkspace.GetPath("SeriesOne");
        Directory.CreateDirectory(source1);
        string data1 = _dataLocation.GetDataRoot(project1Id);
        Directory.CreateDirectory(data1);

        var descriptor1 = new WorkspaceDescriptor(
            FormatVersion: 1,
            ProjectId: project1Id,
            ProjectName: "Series One Project",
            SeriesName: "Series One",
            SourceRoot: source1,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            LastOpenedAt: DateTimeOffset.UtcNow,
            IsMigratedFromLegacy: false);
        await File.WriteAllTextAsync(
            Path.Combine(data1, "workspace.json"),
            JsonSerializer.Serialize(descriptor1),
            TestContext.Current.CancellationToken);

        Guid project2Id = Guid.NewGuid();
        string source2 = _temporaryWorkspace.GetPath("SeriesTwo");
        Directory.CreateDirectory(source2);
        string data2 = _dataLocation.GetDataRoot(project2Id);
        Directory.CreateDirectory(data2);

        var descriptor2 = new WorkspaceDescriptor(
            FormatVersion: 1,
            ProjectId: project2Id,
            ProjectName: "Series Two Project",
            SeriesName: "Series Two",
            SourceRoot: source2,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            LastOpenedAt: DateTimeOffset.UtcNow,
            IsMigratedFromLegacy: false);
        await File.WriteAllTextAsync(
            Path.Combine(data2, "workspace.json"),
            JsonSerializer.Serialize(descriptor2),
            TestContext.Current.CancellationToken);

        // 2. Register both in registry initially
        await _registry.RegisterOrUpdateAsync(new WorkspaceRegistryEntry(
            project1Id, descriptor1.ProjectName, descriptor1.SeriesName, source1, data1,
            descriptor1.CreatedAt, descriptor1.UpdatedAt, descriptor1.LastOpenedAt ?? descriptor1.UpdatedAt, false), TestContext.Current.CancellationToken);
        await _registry.RegisterOrUpdateAsync(new WorkspaceRegistryEntry(
            project2Id, descriptor2.ProjectName, descriptor2.SeriesName, source2, data2,
            descriptor2.CreatedAt, descriptor2.UpdatedAt, descriptor2.LastOpenedAt ?? descriptor2.UpdatedAt, false), TestContext.Current.CancellationToken);

        Assert.True(File.Exists(_dataLocation.RegistryFilePath));

        // 3. Intentionally corrupt the registry file
        await File.WriteAllTextAsync(_dataLocation.RegistryFilePath, "{ NOT VALID JSON %&#$*", TestContext.Current.CancellationToken);

        // 4. Instantiate a new registry instance pointing to same location
        var newRegistry = new JsonWorkspaceRegistry(_dataLocation);
        IReadOnlyList<WorkspaceRegistryEntry> all = await newRegistry.GetAllAsync(TestContext.Current.CancellationToken);

        // 5. Verify backup was created
        string appDataDir = Path.GetDirectoryName(_dataLocation.RegistryFilePath)!;
        string[] backupFiles = Directory.GetFiles(appDataDir, "workspaces.json.corrupt-*");
        Assert.NotEmpty(backupFiles);

        // 6. Verify reconstruction from workspace.json files
        Assert.Equal(2, all.Count);
        Assert.Contains(all, entry => entry.ProjectId == project1Id && entry.ProjectName == "Series One Project");
        Assert.Contains(all, entry => entry.ProjectId == project2Id && entry.ProjectName == "Series Two Project");

        // 7. Verify existing DataRoots were NEVER deleted
        Assert.True(Directory.Exists(data1));
        Assert.True(Directory.Exists(data2));
    }

    [Fact]
    public async Task RelocateSourceAsync_ShouldUpdateSourceRoot()
    {
        Guid projectId = Guid.NewGuid();
        string sourceRoot = _temporaryWorkspace.GetPath("OldSource");
        Directory.CreateDirectory(sourceRoot);
        string dataRoot = _dataLocation.GetDataRoot(projectId);
        Directory.CreateDirectory(dataRoot);

        var now = DateTimeOffset.UtcNow;
        var entry = new WorkspaceRegistryEntry(
            projectId, "Relocated Project", "Series", sourceRoot, dataRoot, now, now, now, false);
        await _registry.RegisterOrUpdateAsync(entry, TestContext.Current.CancellationToken);

        string newSourceRoot = _temporaryWorkspace.GetPath("NewSource");
        Directory.CreateDirectory(newSourceRoot);

        await _registry.RelocateSourceAsync(projectId, newSourceRoot, TestContext.Current.CancellationToken);

        WorkspaceRegistryEntry? updated = await _registry.FindByIdAsync(projectId, TestContext.Current.CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal(Path.GetFullPath(newSourceRoot), updated.SourceRoot);
    }

    [Fact]
    public async Task UnregisterAsync_ShouldRemoveFromRegistry_WithoutDeletingDataRoot()
    {
        Guid projectId = Guid.NewGuid();
        string sourceRoot = _temporaryWorkspace.GetPath("UnregisteredSource");
        Directory.CreateDirectory(sourceRoot);
        string dataRoot = _dataLocation.GetDataRoot(projectId);
        Directory.CreateDirectory(dataRoot);

        var now = DateTimeOffset.UtcNow;
        var entry = new WorkspaceRegistryEntry(
            projectId, "To Remove", "Series", sourceRoot, dataRoot, now, now, now, false);
        await _registry.RegisterOrUpdateAsync(entry, TestContext.Current.CancellationToken);

        await _registry.RemoveAsync(projectId, TestContext.Current.CancellationToken);

        WorkspaceRegistryEntry? removed = await _registry.FindByIdAsync(projectId, TestContext.Current.CancellationToken);
        Assert.Null(removed);

        // DataRoot must NOT be deleted
        Assert.True(Directory.Exists(dataRoot));
    }

    public void Dispose() => _temporaryWorkspace.Dispose();
}
