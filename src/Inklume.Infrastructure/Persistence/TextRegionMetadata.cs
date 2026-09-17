using Inklume.Domain.TextRegions;

namespace Inklume.Infrastructure.Persistence;

internal sealed class TextRegionMetadata
{
    public Guid Id { get; set; }

    public Guid PageId { get; set; }

    public TextRegionGeometryKind GeometryKind { get; set; }

    public int ReadingOrder { get; set; }

    public TextRegionRole Role { get; set; }

    public TextContainerType ContainerType { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public TextRegionOrigin Origin { get; set; }

    public string? ReviewedText { get; set; }

    public TextRegionReviewStatus ReviewStatus { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public DateTimeOffset? UserModifiedAt { get; set; }

    public List<TextRegionPointMetadata> Points { get; set; } = [];
}
