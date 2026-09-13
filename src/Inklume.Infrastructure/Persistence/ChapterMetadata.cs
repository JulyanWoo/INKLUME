namespace Inklume.Infrastructure.Persistence;

internal sealed class ChapterMetadata
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public decimal Number { get; set; }

    public string? Title { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
