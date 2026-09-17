namespace Inklume.Desktop.VisualEditor;

public static class ZoomLevels
{
    public const double MinimumManualZoom = 0.10;
    public const double MaximumManualZoom = 64.00;
    public const double DefaultZoom = 1.00;

    public static readonly double[] Levels =
    [
        0.10,
        0.125,
        0.167,
        0.25,
        0.333,
        0.50,
        0.667,
        0.75,
        1.00,
        1.25,
        1.50,
        2.00,
        3.00,
        4.00,
        6.00,
        8.00,
        12.00,
        16.00,
        24.00,
        32.00,
        48.00,
        64.00
    ];

    public static double GetNextZoomIn(double currentZoom)
    {
        foreach (double level in Levels)
        {
            if (level > currentZoom + 0.0001)
            {
                return level;
            }
        }

        return MaximumManualZoom;
    }

    public static double GetNextZoomOut(double currentZoom)
    {
        for (int i = Levels.Length - 1; i >= 0; i--)
        {
            if (Levels[i] < currentZoom - 0.0001)
            {
                return Levels[i];
            }
        }

        return MinimumManualZoom;
    }
}
