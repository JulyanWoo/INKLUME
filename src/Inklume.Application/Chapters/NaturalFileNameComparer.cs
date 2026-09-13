namespace Inklume.Application.Chapters;

public sealed class NaturalFileNameComparer : IComparer<string>
{
    public static NaturalFileNameComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        int leftIndex = 0;
        int rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (char.IsAsciiDigit(left[leftIndex]) && char.IsAsciiDigit(right[rightIndex]))
            {
                int comparison = CompareNumber(left, ref leftIndex, right, ref rightIndex);
                if (comparison != 0)
                {
                    return comparison;
                }

                continue;
            }

            int characterComparison = char.ToUpperInvariant(left[leftIndex]).CompareTo(char.ToUpperInvariant(right[rightIndex]));
            if (characterComparison != 0)
            {
                return characterComparison;
            }

            leftIndex++;
            rightIndex++;
        }

        int lengthComparison = (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
        return lengthComparison != 0 ? lengthComparison : StringComparer.Ordinal.Compare(left, right);
    }

    private static int CompareNumber(string left, ref int leftIndex, string right, ref int rightIndex)
    {
        int leftStart = leftIndex;
        int rightStart = rightIndex;
        while (leftIndex < left.Length && char.IsAsciiDigit(left[leftIndex]))
        {
            leftIndex++;
        }

        while (rightIndex < right.Length && char.IsAsciiDigit(right[rightIndex]))
        {
            rightIndex++;
        }

        int leftSignificant = leftStart;
        int rightSignificant = rightStart;
        while (leftSignificant < leftIndex - 1 && left[leftSignificant] == '0')
        {
            leftSignificant++;
        }

        while (rightSignificant < rightIndex - 1 && right[rightSignificant] == '0')
        {
            rightSignificant++;
        }

        int lengthComparison = (leftIndex - leftSignificant).CompareTo(rightIndex - rightSignificant);
        if (lengthComparison != 0)
        {
            return lengthComparison;
        }

        int valueComparison = left.AsSpan(leftSignificant, leftIndex - leftSignificant)
            .SequenceCompareTo(right.AsSpan(rightSignificant, rightIndex - rightSignificant));
        if (valueComparison != 0)
        {
            return valueComparison;
        }

        return (leftIndex - leftStart).CompareTo(rightIndex - rightStart);
    }
}
