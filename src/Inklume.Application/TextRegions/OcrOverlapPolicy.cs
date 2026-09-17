using Inklume.Domain.TextRegions;

namespace Inklume.Application.TextRegions;

/// <summary>
/// Centralized overlap suppression policy for OCR page re-runs.
/// </summary>
public static class OcrOverlapPolicy
{
    /// <summary>
    /// Default IoU threshold for suppressing new automatic detections that overlap protected regions.
    /// Recommended starting threshold of 0.50 prevents duplicate OCR bounding boxes while allowing distinct nearby text.
    /// </summary>
    public const double DefaultOverlapSuppressionThreshold = 0.50;

    /// <summary>
    /// Calculates the Intersection over Union (IoU) of two axis-aligned bounding boxes.
    /// </summary>
    public static double CalculateBoundsIoU(ImageBounds first, ImageBounds second)
    {
        double x1 = Math.Max(first.X, second.X);
        double y1 = Math.Max(first.Y, second.Y);
        double x2 = Math.Min(first.Right, second.Right);
        double y2 = Math.Min(first.Bottom, second.Bottom);

        double intersectionWidth = Math.Max(0.0, x2 - x1);
        double intersectionHeight = Math.Max(0.0, y2 - y1);
        double intersectionArea = intersectionWidth * intersectionHeight;

        if (intersectionArea <= 0.0)
        {
            return 0.0;
        }

        double firstArea = first.Width * first.Height;
        double secondArea = second.Width * second.Height;
        double unionArea = firstArea + secondArea - intersectionArea;

        return unionArea <= 0.0 ? 0.0 : intersectionArea / unionArea;
    }

    /// <summary>
    /// Determines whether a candidate detection should be suppressed due to overlapping any protected region.
    /// </summary>
    public static bool ShouldSuppressDetection(
        ImageBounds candidate,
        IEnumerable<ImageBounds> protectedBounds,
        double threshold = DefaultOverlapSuppressionThreshold)
    {
        ArgumentNullException.ThrowIfNull(protectedBounds);
        foreach (ImageBounds protectedBound in protectedBounds)
        {
            if (CalculateBoundsIoU(candidate, protectedBound) >= threshold)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a text region is protected from automatic replacement during Page OCR re-run.
    /// Protected if Manual origin or user-edited (UserModifiedAt, Reviewed status, ReviewedText override, or non-Unknown Role/Container).
    /// </summary>
    public static bool IsProtected(TextRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return region.Origin == TextRegionOrigin.Manual
            || region.UserModifiedAt != null
            || region.ReviewStatus == TextRegionReviewStatus.Reviewed
            || region.ReviewedText != null
            || region.Role != TextRegionRole.Unknown
            || region.ContainerType != TextContainerType.Unknown;
    }
}
