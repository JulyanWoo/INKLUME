using System.Text.Json;
using Inklume.Application.Projects;
using Inklume.Application.Workspaces;
using Inklume.Domain.Projects;
using Inklume.Infrastructure.Projects;
using Inklume.Infrastructure.Workspaces;
using Xunit;

namespace Inklume.Infrastructure.Tests;

public sealed class WorkspaceDataRootTests : IDisposable
{
    private static readonly byte[] SamplePngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly TemporaryWorkspace _temporaryWorkspace = new();
    private readonly IWorkspaceDataLocation _dataLocation;
    private readonly IWorkspaceRegistry _registry;
    private readonly FileSystemProjectStore _store;

    public WorkspaceDataRootTests()
    {
        _dataLocation = new DefaultWorkspaceDataLocation(Path.Combine(_temporaryWorkspace.RootPath, "AppData"));
        _registry = new JsonWorkspaceRegistry(_dataLocation);
        _store = new FileSystemProjectStore(_dataLocation, _registry);
    }

    [Fact]
    public async Task OpenAsync_ShouldCreateCompleteDataRootStructure_AndZeroFilesInsideSourceRoot()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("UserSeriesFolder");
        Directory.CreateDirectory(sourceRoot);

        string chFolder = Path.Combine(sourceRoot, "001");
        Directory.CreateDirectory(chFolder);
        await File.WriteAllBytesAsync(Path.Combine(chFolder, "01.png"), SamplePngBytes, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "info.txt"), "User notes", TestContext.Current.CancellationToken);

        ProjectWorkspace workspace = await _store.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);

        // Verify DataRoot location and contents
        Assert.Equal(_dataLocation.GetDataRoot(workspace.Project.Id), workspace.DataRoot);
        Assert.True(Directory.Exists(workspace.DataRoot), "DataRoot directory was not created.");
        Assert.True(File.Exists(Path.Combine(workspace.DataRoot, "workspace.json")), "workspace.json was not created in DataRoot.");
        Assert.True(File.Exists(workspace.DatabasePath), "workspace.db was not created in DataRoot.");
        Assert.True(Directory.Exists(workspace.ContextRoot), "context directory was not created in DataRoot.");
        Assert.True(Directory.Exists(workspace.CacheRoot), "cache directory was not created in DataRoot.");

        // Context JSON files created in DataRoot
        Assert.True(File.Exists(Path.Combine(workspace.ContextRoot, "series.json")));
        Assert.True(File.Exists(Path.Combine(workspace.ContextRoot, "characters.json")));
        Assert.True(File.Exists(Path.Combine(workspace.ContextRoot, "glossary.json")));
        Assert.True(File.Exists(Path.Combine(workspace.ContextRoot, "translation_rules.json")));

        // Zero INKLUME management files created inside user sourceRoot
        Assert.False(File.Exists(Path.Combine(sourceRoot, "workspace.json")));
        Assert.False(File.Exists(Path.Combine(sourceRoot, "workspace.db")));
        Assert.False(File.Exists(Path.Combine(sourceRoot, "project.db")));
        Assert.False(Directory.Exists(Path.Combine(sourceRoot, "context")));
        Assert.False(Directory.Exists(Path.Combine(sourceRoot, "cache")));
        Assert.False(Directory.Exists(Path.Combine(sourceRoot, "artifacts")));
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateCompleteDataRootStructure_AndLeaveSourceRootClean()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("NewCreatedSeries");
        var now = DateTimeOffset.UtcNow;
        var project = new TranslationProject(Guid.NewGuid(), "New Project", "New Series", now, now);

        ProjectWorkspace workspace = await _store.CreateAsync(project, sourceRoot, TestContext.Current.CancellationToken);

        Assert.Equal(sourceRoot, workspace.SourceRoot);
        Assert.Equal(_dataLocation.GetDataRoot(project.Id), workspace.DataRoot);

        // DataRoot structure
        Assert.True(File.Exists(Path.Combine(workspace.DataRoot, "workspace.json")));
        Assert.True(File.Exists(workspace.DatabasePath));
        Assert.True(Directory.Exists(workspace.ContextRoot));
        Assert.True(Directory.Exists(workspace.CacheRoot));

        // SourceRoot has NO management artifacts
        Assert.False(File.Exists(Path.Combine(sourceRoot, "workspace.json")));
        Assert.False(File.Exists(Path.Combine(sourceRoot, "workspace.db")));
        Assert.False(File.Exists(Path.Combine(sourceRoot, "project.db")));
        Assert.False(Directory.Exists(Path.Combine(sourceRoot, "context")));
        Assert.False(Directory.Exists(Path.Combine(sourceRoot, "cache")));
        Assert.False(Directory.Exists(Path.Combine(sourceRoot, "chapters")));
    }

    [Fact]
    public async Task WorkspaceDescriptor_ShouldSerializeAndDeserializeValidMetadata()
    {
        string sourceRoot = _temporaryWorkspace.GetPath("DescriptorTestFolder");
        Directory.CreateDirectory(sourceRoot);

        ProjectWorkspace workspace = await _store.OpenAsync(sourceRoot, TestContext.Current.CancellationToken);

        string descriptorPath = Path.Combine(workspace.DataRoot, "workspace.json");
        Assert.True(File.Exists(descriptorPath));

        string json = await File.ReadAllTextAsync(descriptorPath, TestContext.Current.CancellationToken);
        WorkspaceDescriptor? descriptor = JsonSerializer.Deserialize<WorkspaceDescriptor>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(descriptor);
        Assert.Equal(workspace.Project.Id, descriptor.ProjectId);
        Assert.Equal(workspace.Project.Name, descriptor.ProjectName);
        Assert.Equal(workspace.Project.SeriesName, descriptor.SeriesName);
        Assert.Equal(Path.GetFullPath(sourceRoot), Path.GetFullPath(descriptor.SourceRoot));
        Assert.False(descriptor.IsMigratedFromLegacy);
    }

    public void Dispose() => _temporaryWorkspace.Dispose();
}
