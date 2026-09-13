using Inklume.Domain.Projects;

namespace Inklume.Application.Chapters;

public sealed record ImportChapterRequest(ChapterNumber Number, string? Title, ChapterSource Source);
