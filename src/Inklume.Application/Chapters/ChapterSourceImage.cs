namespace Inklume.Application.Chapters;

public sealed record ChapterSourceImage
{
    public ChapterSourceImage(
        string originalFileName,
        string fileExtension,
        Func<CancellationToken, ValueTask<Stream>> openReadAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileExtension);
        ArgumentNullException.ThrowIfNull(openReadAsync);
        OriginalFileName = originalFileName;
        FileExtension = fileExtension;
        OpenReadAsync = openReadAsync;
    }

    public string OriginalFileName { get; }

    public string FileExtension { get; }

    public Func<CancellationToken, ValueTask<Stream>> OpenReadAsync { get; }
}
