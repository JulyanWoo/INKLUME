using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Inklume.Desktop.Services;

public sealed class WpfPagePreviewLoader : IPagePreviewLoader
{
    private const int PreviewDecodeWidth = 1600;
    private const int MaximumDecodedPixels = 8_000_000;

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
            double pixelBudgetScale = Math.Sqrt(MaximumDecodedPixels / ((double)rawPixelWidth * rawPixelHeight));
            double scale = Math.Min(1, Math.Min((double)PreviewDecodeWidth / rawPixelWidth, pixelBudgetScale));
            if (scale < 1)
            {
                image.DecodePixelWidth = Math.Max(1, (int)Math.Round(rawPixelWidth * scale));
            }
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
            int rawWidth = 0;
            int rawHeight = 0;

            try
            {
                using var headerStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = BitmapDecoder.Create(headerStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count > 0)
                {
                    rawWidth = decoder.Frames[0].PixelWidth;
                    rawHeight = decoder.Frames[0].PixelHeight;
                }
            }
            catch
            {
                // Fall back if decoder fails to parse headers without full decode
            }

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;

            if (rawWidth > 0 && rawHeight > 0)
            {
                double pixelBudgetScale = Math.Sqrt(MaximumDecodedPixels / ((double)rawWidth * rawHeight));
                double scale = Math.Min(1, Math.Min((double)PreviewDecodeWidth / rawWidth, pixelBudgetScale));
                if (scale < 1)
                {
                    image.DecodePixelWidth = Math.Max(1, (int)Math.Round(rawWidth * scale));
                }
            }

            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            cancellationToken.ThrowIfCancellationRequested();

            int finalRawWidth = rawWidth > 0 ? rawWidth : image.PixelWidth;
            int finalRawHeight = rawHeight > 0 ? rawHeight : image.PixelHeight;

            return new PagePreview(image, image.PixelWidth, image.PixelHeight, finalRawWidth, finalRawHeight);
        }, cancellationToken);
    }
}
