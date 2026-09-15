namespace Inklume.Application.Workspaces;

public interface IWorkspaceDataLocation
{
    string WorkspacesRoot { get; }

    string RegistryFilePath { get; }

    string GetDataRoot(Guid projectId);
}
