using Inklume.Application.Projects;
using Inklume.Domain.Projects;

namespace Inklume.Application.Chapters;

public sealed record ChapterImportResult(
    ProjectWorkspace Workspace,
    Chapter Chapter,
    IReadOnlyList<PageWorkspace> Pages,
    IReadOnlyList<string> IgnoredFiles);
