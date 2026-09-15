using Inklume.Application.Projects;
using Inklume.Domain.Projects;

namespace Inklume.Application.Chapters;

public sealed record ChapterIndexResult(
    ProjectWorkspace Workspace,
    Chapter Chapter,
    IReadOnlyList<PageWorkspace> Pages);
