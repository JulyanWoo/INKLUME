using System.Windows.Media;

namespace Inklume.Desktop.Services;

public sealed record PagePreview(
    ImageSource Image,
    int DecodedPixelWidth,
    int DecodedPixelHeight,
    int RawPixelWidth = 0,
    int RawPixelHeight = 0);
