using Inklume.Application.Chapters;
using Xunit;

namespace Inklume.Application.Tests;

public sealed class NaturalFileNameComparerTests
{
    [Theory]
    [MemberData(nameof(FileNameSets))]
    public void OrderBy_ShouldSortNumericSegmentsNaturally(string[] input, string[] expected)
    {
        string[] ordered = [.. input.OrderBy(value => value, NaturalFileNameComparer.Instance)];

        Assert.Equal(expected, ordered);
    }

    public static TheoryData<string[], string[]> FileNameSets => new()
    {
        { ["1.png", "10.png", "2.png"], ["1.png", "2.png", "10.png"] },
        { ["page_10.png", "page_2.png", "page_1.png"], ["page_1.png", "page_2.png", "page_10.png"] },
        { ["page01.png", "page1.png", "page001.png"], ["page1.png", "page01.png", "page001.png"] }
    };
}
