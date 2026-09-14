namespace Inklume.Domain.Projects;

public sealed record Page
{
    public const int MaximumOriginalFileNameLength = 255;
    public const int MaximumRelativePathLength = 1024;
    public const int Sha256Length = 64;

    public Page(
        Guid id,
        Guid chapterId,
        int number,
        string originalFileName,
        string relativePath,
        string contentHash,
        int? pixelWidth = null,
        int? pixelHeight = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The page identifier must not be empty.", nameof(id));
        }

        if (chapterId == Guid.Empty)
        {
            throw new ArgumentException("The chapter identifier must not be empty.", nameof(chapterId));
        }

        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "The page number must be positive.");
        }

        Id = id;
        ChapterId = chapterId;
        Number = number;
        OriginalFileName = ValidateText(originalFileName, MaximumOriginalFileNameLength, nameof(originalFileName));
        RelativePath = ValidateText(relativePath, MaximumRelativePathLength, nameof(relativePath));
        ContentHash = ValidateHash(contentHash);
        ValidateDimensions(pixelWidth, pixelHeight);
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    public Guid Id { get; }

    public Guid ChapterId { get; }

    public int Number { get; }

    public string OriginalFileName { get; }

    public string RelativePath { get; }

    public string ContentHash { get; }

    public int? PixelWidth { get; }

    public int? PixelHeight { get; }

    public bool HasDimensions => PixelWidth.HasValue && PixelHeight.HasValue;

    public Page WithDimensions(int pixelWidth, int pixelHeight)
    {
        ValidateDimensions(pixelWidth, pixelHeight);
        if (HasDimensions)
        {
            if (PixelWidth != pixelWidth || PixelHeight != pixelHeight)
            {
                throw new InvalidOperationException("The dimensions of an immutable RAW page cannot be changed.");
            }

            return this;
        }

        return new Page(Id, ChapterId, Number, OriginalFileName, RelativePath, ContentHash, pixelWidth, pixelHeight);
    }

    private static string ValidateText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"The value must contain at most {maximumLength} characters and no control characters.", parameterName);
        }

        return value;
    }

    private static string ValidateHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != Sha256Length || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("The content hash must be a 64-character SHA-256 hexadecimal value.", nameof(value));
        }

        return value.ToUpperInvariant();
    }

    private static void ValidateDimensions(int? pixelWidth, int? pixelHeight)
    {
        if (pixelWidth.HasValue != pixelHeight.HasValue)
        {
            throw new ArgumentException("Page width and height must either both be set or both be absent.");
        }

        if (pixelWidth is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Page width must be positive.");
        }

        if (pixelHeight is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight), "Page height must be positive.");
        }
    }
}
