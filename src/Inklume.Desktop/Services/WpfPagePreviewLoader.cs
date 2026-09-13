using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Inklume.Desktop.Services;

public sealed class WpfPagePreviewLoader : IPagePreviewLoader
{
    private const int PreviewDecodeWidth = 1600;

    public Task<ImageSource> LoadAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Task.Run<ImageSource>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.DecodePixelWidth = PreviewDecodeWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            cancellationToken.ThrowIfCancellationRequested();
            return image;
        }, cancellationToken);
    }
}
