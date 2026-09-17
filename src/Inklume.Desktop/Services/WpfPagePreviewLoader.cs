using System.IO;
using System.Windows.Media.Imaging;

namespace Inklume.Desktop.Services;

public sealed class WpfPagePreviewLoader : IPagePreviewLoader
{
    public Task<PagePreview> LoadAsync(
        string filePath,
        int rawPixelWidth,
        int rawPixelHeight,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (rawPixelWidth <= 0 || rawPixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rawPixelWidth), "RAW image dimensions must be positive.");
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            cancellationToken.ThrowIfCancellationRequested();
            return new PagePreview(image, image.PixelWidth, image.PixelHeight, rawPixelWidth, rawPixelHeight);
        }, cancellationToken);
    }

    public Task<PagePreview> LoadImageAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            cancellationToken.ThrowIfCancellationRequested();

            return new PagePreview(image, image.PixelWidth, image.PixelHeight, image.PixelWidth, image.PixelHeight);
        }, cancellationToken);
    }
}
