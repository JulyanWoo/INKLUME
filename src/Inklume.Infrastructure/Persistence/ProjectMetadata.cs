namespace Inklume.Infrastructure.Persistence;

internal sealed class ProjectMetadata
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string SeriesName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int FormatVersion { get; set; }
}
