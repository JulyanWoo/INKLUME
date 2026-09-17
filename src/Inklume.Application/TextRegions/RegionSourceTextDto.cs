using Inklume.Domain.TextRegions;

namespace Inklume.Application.TextRegions;

public sealed record RegionSourceTextDto(
    Guid RegionId,
    string? RawOcrText,
    string? ReviewedText,
    TextRegionReviewStatus ReviewStatus,
    TextRegionRole Role,
    TextContainerType ContainerType,
    int ReadingOrder)
{
    public string? EffectiveSourceText => ResolveEffectiveSourceText(ReviewedText, RawOcrText);

    public static string? ResolveEffectiveSourceText(string? reviewedText, string? rawOcrText)
        => reviewedText ?? rawOcrText;

    public bool HasReviewedOverride => ReviewedText is not null;

    public bool HasOcr => RawOcrText is not null;
}
