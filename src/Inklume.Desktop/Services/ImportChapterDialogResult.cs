using Inklume.Domain.Projects;

namespace Inklume.Desktop.Services;

public sealed record ImportChapterDialogResult(
    ChapterNumber Number,
    string? Title,
    string SourceFolder);
