using System.Globalization;

namespace Inklume.Domain.Projects;

public readonly record struct ChapterNumber : IComparable<ChapterNumber>
{
    public ChapterNumber(decimal value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The chapter number must not be negative.");
        }

        Value = value;
    }

    public decimal Value { get; }

    public int CompareTo(ChapterNumber other) => Value.CompareTo(other.Value);

    public string ToFolderName()
    {
        string value = ToString();
        int separatorIndex = value.IndexOf('.');
        string integerPart = separatorIndex < 0 ? value : value[..separatorIndex];
        string fractionalPart = separatorIndex < 0 ? string.Empty : value[separatorIndex..];
        return string.Concat(integerPart.PadLeft(3, '0'), fractionalPart);
    }

    public override string ToString() => Value.ToString("0.############################", CultureInfo.InvariantCulture);

    public static ChapterNumber Parse(string value)
    {
        if (!TryParse(value, out ChapterNumber number))
        {
            throw new FormatException("Enter a non-negative chapter number, such as 1 or 10.5.");
        }

        return number;
    }

    public static bool TryParse(string? value, out ChapterNumber number)
    {
        const NumberStyles styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
        if (decimal.TryParse(value, styles, CultureInfo.InvariantCulture, out decimal parsed)
            && parsed >= 0)
        {
            number = new ChapterNumber(parsed);
            return true;
        }

        number = default;
        return false;
    }
}
