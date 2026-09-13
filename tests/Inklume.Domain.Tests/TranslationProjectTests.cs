using Inklume.Domain.Projects;
using Xunit;

namespace Inklume.Domain.Tests;

public sealed class TranslationProjectTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_ShouldPreserveIdentityAndTrimNames_WhenMetadataIsValid()
    {
        var projectId = Guid.NewGuid();
        DateTimeOffset updatedAt = CreatedAt.AddMinutes(5);

        var project = new TranslationProject(projectId, "  Moonlight Edition  ", "  Moonlight  ", CreatedAt, updatedAt);

        Assert.Equal(projectId, project.Id);
        Assert.Equal("Moonlight Edition", project.Name);
        Assert.Equal("Moonlight", project.SeriesName);
        Assert.Equal(CreatedAt, project.CreatedAt);
        Assert.Equal(updatedAt, project.UpdatedAt);
    }

    [Fact]
    public void Constructor_ShouldPreserveUnicode_WhenNamesContainInternationalCharacters()
    {
        var project = new TranslationProject(Guid.NewGuid(), "달빛 漫画 Édition", "月の物語", CreatedAt, CreatedAt);

        Assert.Equal("달빛 漫画 Édition", project.Name);
        Assert.Equal("月の物語", project.SeriesName);
    }

    [Fact]
    public void Constructor_ShouldNormalizeTimestampsToUtc_WhenOffsetsAreProvided()
    {
        DateTimeOffset localCreatedAt = CreatedAt.ToOffset(TimeSpan.FromHours(-5));
        DateTimeOffset localUpdatedAt = CreatedAt.AddHours(1).ToOffset(TimeSpan.FromHours(9));

        var project = new TranslationProject(Guid.NewGuid(), "Project", "Series", localCreatedAt, localUpdatedAt);

        Assert.Equal(CreatedAt, project.CreatedAt);
        Assert.Equal(CreatedAt.AddHours(1), project.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, project.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, project.UpdatedAt.Offset);
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyIdentifier_WhenCreatingProject()
    {
        Assert.Throws<ArgumentException>("id", () =>
            new TranslationProject(Guid.Empty, "Project", "Series", CreatedAt, CreatedAt));
    }

    [Theory]
    [InlineData("", "Series", "name")]
    [InlineData("   ", "Series", "name")]
    [InlineData("Project", "", "seriesName")]
    [InlineData("Project", "   ", "seriesName")]
    [InlineData("Project\nName", "Series", "name")]
    [InlineData("Project", "Series\tName", "seriesName")]
    [InlineData("\tProject", "Series", "name")]
    [InlineData("Project", "Series\0", "seriesName")]
    public void Constructor_ShouldRejectInvalidNames_WhenBlankOrContainingControlCharacters(
        string name,
        string seriesName,
        string parameterName)
    {
        Assert.Throws<ArgumentException>(parameterName, () =>
            new TranslationProject(Guid.NewGuid(), name, seriesName, CreatedAt, CreatedAt));
    }

    [Fact]
    public void Constructor_ShouldRejectNullName_WhenCreatingProject()
    {
        Assert.Throws<ArgumentNullException>("name", () =>
            new TranslationProject(Guid.NewGuid(), null!, "Series", CreatedAt, CreatedAt));
    }

    [Fact]
    public void Constructor_ShouldRejectNullSeriesName_WhenCreatingProject()
    {
        Assert.Throws<ArgumentNullException>("seriesName", () =>
            new TranslationProject(Guid.NewGuid(), "Project", null!, CreatedAt, CreatedAt));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_ShouldRejectOverlongNames_WhenLimitIsExceeded(bool isProjectName)
    {
        string overlongName = new('a', TranslationProject.MaximumNameLength + 1);
        string name = isProjectName ? overlongName : "Project";
        string seriesName = isProjectName ? "Series" : overlongName;

        Assert.Throws<ArgumentException>(isProjectName ? "name" : "seriesName", () =>
            new TranslationProject(Guid.NewGuid(), name, seriesName, CreatedAt, CreatedAt));
    }

    [Fact]
    public void Constructor_ShouldAcceptNamesAtLimit_WhenSurroundingSpacesAreTrimmed()
    {
        string name = new('a', TranslationProject.MaximumNameLength);

        var project = new TranslationProject(Guid.NewGuid(), $" {name} ", name, CreatedAt, CreatedAt);

        Assert.Equal(name, project.Name);
        Assert.Equal(name, project.SeriesName);
    }

    [Fact]
    public void Constructor_ShouldRejectEarlierUpdate_WhenTimestampPrecedesCreation()
    {
        Assert.Throws<ArgumentException>("updatedAt", () =>
            new TranslationProject(Guid.NewGuid(), "Project", "Series", CreatedAt, CreatedAt.AddTicks(-1)));
    }
}
