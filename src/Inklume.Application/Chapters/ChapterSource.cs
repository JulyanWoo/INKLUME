namespace Inklume.Application.Chapters;

public sealed record ChapterSource(
    IReadOnlyList<ChapterSourceImage> Images,
    IReadOnlyList<string> IgnoredFiles);
