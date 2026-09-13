namespace Inklume.Application.Chapters;

public interface ILocalChapterSourceProvider
{
    Task<ChapterSource> LoadAsync(
        string sourceFolder,
        string projectRoot,
        CancellationToken cancellationToken);
}
