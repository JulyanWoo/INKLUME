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
        DateTimeOffset updatedAt)
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
    }

    public Guid Id { get; }

    public Guid PageId { get; }

    public TextRegionGeometry Geometry { get; }

    public int ReadingOrder { get; }

    public TextRegionRole Role { get; }

    public TextContainerType ContainerType { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public TextRegion WithClassification(
        TextRegionRole role,
        TextContainerType containerType,
        DateTimeOffset updatedAt)
        => new(Id, PageId, Geometry, ReadingOrder, role, containerType, CreatedAt, updatedAt);
}
