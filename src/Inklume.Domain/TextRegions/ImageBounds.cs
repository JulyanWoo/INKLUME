namespace Inklume.Domain.TextRegions;

public readonly record struct ImageBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}
