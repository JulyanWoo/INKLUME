using Inklume.Domain.Projects;

namespace Inklume.Application.Projects;

public sealed record ProjectWorkspace(
    TranslationProject Project,
    string SourceRoot,
    string DataRoot)
{
    public string DatabasePath => Path.Combine(DataRoot, "workspace.db");

    public string ContextRoot => Path.Combine(DataRoot, "context");

    public string CacheRoot => Path.Combine(DataRoot, "cache");

    public string ArtifactsRoot => Path.Combine(DataRoot, "artifacts");
}
