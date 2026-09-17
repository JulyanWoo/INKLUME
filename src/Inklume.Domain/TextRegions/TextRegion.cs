namespace Inklume.Domain.TextRegions;

public sealed record TextRegion
{
    public TextRegion(
        Guid id,
        Guid pageId,
        TextRegionGeometry geometry,
        int readingOrder,
        TextRegionRole role,
        TextContainerType containerType,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        TextRegionOrigin origin = TextRegionOrigin.Manual,
        string? reviewedText = null,
        TextRegionReviewStatus reviewStatus = TextRegionReviewStatus.Pending,
        DateTimeOffset? reviewedAt = null,
        DateTimeOffset? userModifiedAt = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The text region identifier must not be empty.", nameof(id));
        }

        if (pageId == Guid.Empty)
        {
            throw new ArgumentException("The page identifier must not be empty.", nameof(pageId));
        }

        ArgumentNullException.ThrowIfNull(geometry);
        if (readingOrder <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(readingOrder), "Reading order must be positive.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), "The text region role is not supported.");
        }

        if (!Enum.IsDefined(containerType))
        {
            throw new ArgumentOutOfRangeException(nameof(containerType), "The text container type is not supported.");
        }

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), "The text region origin is not supported.");
        }

        if (!Enum.IsDefined(reviewStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(reviewStatus), "The text region review status is not supported.");
        }

        if (updatedAt < createdAt)
        {
            throw new ArgumentException("The update timestamp must not precede creation.", nameof(updatedAt));
        }

        Id = id;
        PageId = pageId;
        Geometry = geometry;
        ReadingOrder = readingOrder;
        Role = role;
        ContainerType = containerType;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = updatedAt.ToUniversalTime();
        Origin = origin;
        ReviewedText = reviewedText;
        ReviewStatus = reviewStatus;
        ReviewedAt = reviewedAt?.ToUniversalTime();
        UserModifiedAt = userModifiedAt?.ToUniversalTime();
    }

    public Guid Id { get; }

    public Guid PageId { get; }

    public TextRegionGeometry Geometry { get; }

    public int ReadingOrder { get; }

    public TextRegionRole Role { get; }

    public TextContainerType ContainerType { get; }

    public TextRegionOrigin Origin { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public string? ReviewedText { get; }

    public TextRegionReviewStatus ReviewStatus { get; }

    public DateTimeOffset? ReviewedAt { get; }

    public DateTimeOffset? UserModifiedAt { get; }

    public TextRegion WithClassification(
        TextRegionRole role,
        TextContainerType containerType,
        DateTimeOffset updatedAt,
        DateTimeOffset? userModifiedAt = null)
        => new(
            Id,
            PageId,
            Geometry,
            ReadingOrder,
            role,
            containerType,
            CreatedAt,
            updatedAt,
            Origin,
            ReviewedText,
            ReviewStatus,
            ReviewedAt,
            userModifiedAt ?? UserModifiedAt);

    public TextRegion WithOrigin(
        TextRegionOrigin origin,
        DateTimeOffset updatedAt)
        => new(
            Id,
            PageId,
            Geometry,
            ReadingOrder,
            Role,
            ContainerType,
            CreatedAt,
            updatedAt,
            origin,
            ReviewedText,
            ReviewStatus,
            ReviewedAt,
            UserModifiedAt);

    public TextRegion WithReview(
        string? reviewedText,
        TextRegionReviewStatus reviewStatus,
        DateTimeOffset? reviewedAt,
        DateTimeOffset? userModifiedAt,
        DateTimeOffset updatedAt)
        => new(
            Id,
            PageId,
            Geometry,
            ReadingOrder,
            Role,
            ContainerType,
            CreatedAt,
            updatedAt,
            Origin,
            reviewedText,
            reviewStatus,
            reviewedAt,
            userModifiedAt ?? UserModifiedAt);

    public TextRegion WithReadingOrder(
        int readingOrder,
        DateTimeOffset updatedAt,
        DateTimeOffset? userModifiedAt = null)
        => new(
            Id,
            PageId,
            Geometry,
            readingOrder,
            Role,
            ContainerType,
            CreatedAt,
            updatedAt,
            Origin,
            ReviewedText,
            ReviewStatus,
            ReviewedAt,
            userModifiedAt ?? UserModifiedAt);
}
