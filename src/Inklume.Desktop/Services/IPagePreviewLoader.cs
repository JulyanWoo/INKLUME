namespace Inklume.Desktop.Services;

public interface IPagePreviewLoader
{
    Task<PagePreview> LoadAsync(
        string filePath,
        int rawPixelWidth,
        int rawPixelHeight,
        CancellationToken cancellationToken);
}
