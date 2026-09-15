namespace Inklume.Application.Chapters;

public static class SupportedImageFormats
{
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    };

    public const string DisplayDescription = "PNG, JPEG, and WebP";

    public static bool IsSupported(string? pathOrExtension)
    {
        if (string.IsNullOrWhiteSpace(pathOrExtension))
        {
            return false;
        }

        string extension = Path.GetExtension(pathOrExtension);
        if (string.IsNullOrEmpty(extension) && pathOrExtension.StartsWith('.'))
        {
            extension = pathOrExtension;
        }

        return Extensions.Contains(extension);
    }
}
