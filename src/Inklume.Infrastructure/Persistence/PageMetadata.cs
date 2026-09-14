namespace Inklume.Infrastructure.Persistence;

internal sealed class PageMetadata
{
    public Guid Id { get; set; }

    public Guid ChapterId { get; set; }

    public int Number { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public int? PixelWidth { get; set; }

    public int? PixelHeight { get; set; }
}
