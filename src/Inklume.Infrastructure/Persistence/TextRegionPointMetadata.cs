namespace Inklume.Infrastructure.Persistence;

internal sealed class TextRegionPointMetadata
{
    public Guid TextRegionId { get; set; }

    public int PointIndex { get; set; }

    public double X { get; set; }

    public double Y { get; set; }
}
