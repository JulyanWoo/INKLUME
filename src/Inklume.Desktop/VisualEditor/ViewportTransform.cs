using Inklume.Domain.TextRegions;

namespace Inklume.Desktop.VisualEditor;

public sealed class ViewportTransform
{
    public const double MinimumInteractiveZoom = 0.30;
    public const double MaximumZoom = 2.00;
    private const double MinimumFitZoom = 0.01;
    private const double VisibleMargin = 48;

    private ViewportTransform(
        double imageWidth,
        double imageHeight,
        double viewportWidth,
        double viewportHeight,
        double zoom,
        double offsetX,
        double offsetY)
    {
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        Zoom = zoom;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public double ImageWidth { get; }

    public double ImageHeight { get; }

    public double ViewportWidth { get; }

    public double ViewportHeight { get; }

    public double Zoom { get; }

    public double OffsetX { get; }

    public double OffsetY { get; }

    public static ViewportTransform Fit(
        double imageWidth,
        double imageHeight,
        double viewportWidth,
        double viewportHeight,
        double padding = 32)
    {
        ValidateDimensions(imageWidth, imageHeight, viewportWidth, viewportHeight);
        double availableWidth = Math.Max(1, viewportWidth - (padding * 2));
        double availableHeight = Math.Max(1, viewportHeight - (padding * 2));
        double zoom = Math.Clamp(
            Math.Min(availableWidth / imageWidth, availableHeight / imageHeight),
            MinimumFitZoom,
            MaximumZoom);
        return Centered(imageWidth, imageHeight, viewportWidth, viewportHeight, zoom);
    }

    public ViewportTransform Reset()
        => Centered(ImageWidth, ImageHeight, ViewportWidth, ViewportHeight, 1);

    public ViewportTransform ZoomAt(ViewportPoint cursor, double requestedZoom)
    {
        double zoom = Math.Clamp(requestedZoom, MinimumInteractiveZoom, MaximumZoom);
        ImagePoint imagePoint = ViewportToImage(cursor);
        double offsetX = cursor.X - (imagePoint.X * zoom);
        double offsetY = cursor.Y - (imagePoint.Y * zoom);
        return CreateClamped(zoom, offsetX, offsetY);
    }

    public ViewportTransform PanBy(double horizontalDelta, double verticalDelta)
        => CreateClamped(Zoom, OffsetX + horizontalDelta, OffsetY + verticalDelta);

    public ViewportTransform Resize(double viewportWidth, double viewportHeight)
    {
        ValidateDimensions(ImageWidth, ImageHeight, viewportWidth, viewportHeight);
        ImagePoint center = ViewportToImage(new ViewportPoint(ViewportWidth / 2, ViewportHeight / 2));
        double offsetX = (viewportWidth / 2) - (center.X * Zoom);
        double offsetY = (viewportHeight / 2) - (center.Y * Zoom);
        return new ViewportTransform(
            ImageWidth, ImageHeight, viewportWidth, viewportHeight, Zoom, offsetX, offsetY)
            .CreateClamped(Zoom, offsetX, offsetY);
    }

    public ViewportPoint ImageToViewport(ImagePoint point)
        => new((point.X * Zoom) + OffsetX, (point.Y * Zoom) + OffsetY);

    public ImagePoint ViewportToImage(ViewportPoint point)
        => new((point.X - OffsetX) / Zoom, (point.Y - OffsetY) / Zoom);

    public ImagePoint PreviewToImage(ImagePoint previewPoint, double previewWidth, double previewHeight)
    {
        if (previewWidth <= 0 || previewHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previewWidth), "Preview dimensions must be positive.");
        }

        return new ImagePoint(
            previewPoint.X * ImageWidth / previewWidth,
            previewPoint.Y * ImageHeight / previewHeight);
    }

    private static ViewportTransform Centered(
        double imageWidth,
        double imageHeight,
        double viewportWidth,
        double viewportHeight,
        double zoom)
        => new(
            imageWidth,
            imageHeight,
            viewportWidth,
            viewportHeight,
            zoom,
            (viewportWidth - (imageWidth * zoom)) / 2,
            (viewportHeight - (imageHeight * zoom)) / 2);

    private ViewportTransform CreateClamped(double zoom, double offsetX, double offsetY)
    {
        double scaledWidth = ImageWidth * zoom;
        double scaledHeight = ImageHeight * zoom;
        double clampedX = ClampOffset(offsetX, scaledWidth, ViewportWidth);
        double clampedY = ClampOffset(offsetY, scaledHeight, ViewportHeight);
        return new ViewportTransform(
            ImageWidth, ImageHeight, ViewportWidth, ViewportHeight, zoom, clampedX, clampedY);
    }

    private static double ClampOffset(double offset, double contentLength, double viewportLength)
    {
        if (contentLength <= viewportLength - (VisibleMargin * 2))
        {
            return (viewportLength - contentLength) / 2;
        }

        return Math.Clamp(offset, VisibleMargin - contentLength, viewportLength - VisibleMargin);
    }

    private static void ValidateDimensions(
        double imageWidth,
        double imageHeight,
        double viewportWidth,
        double viewportHeight)
    {
        if (!double.IsFinite(imageWidth) || imageWidth <= 0
            || !double.IsFinite(imageHeight) || imageHeight <= 0
            || !double.IsFinite(viewportWidth) || viewportWidth <= 0
            || !double.IsFinite(viewportHeight) || viewportHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image and viewport dimensions must be finite and positive.");
        }
    }
}
