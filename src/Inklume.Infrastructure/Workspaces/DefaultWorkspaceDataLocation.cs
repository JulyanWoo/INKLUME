using Inklume.Application.Workspaces;

namespace Inklume.Infrastructure.Workspaces;

public sealed class DefaultWorkspaceDataLocation : IWorkspaceDataLocation
{
    private readonly string _baseDirectory;

    public DefaultWorkspaceDataLocation()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "INKLUME"))
    {
    }

    public DefaultWorkspaceDataLocation(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = Path.GetFullPath(baseDirectory);
    }

    public string WorkspacesRoot => Path.Combine(_baseDirectory, "Workspaces");

    public string RegistryFilePath => Path.Combine(_baseDirectory, "workspaces.json");

    public string GetDataRoot(Guid projectId)
        => Path.Combine(WorkspacesRoot, projectId.ToString("N"));
}
