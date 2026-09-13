namespace Inklume.Domain.Projects;

public sealed record Chapter
{
    public const int MaximumTitleLength = 200;

    public Chapter(
        Guid id,
        Guid projectId,
        ChapterNumber number,
        string? title,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The chapter identifier must not be empty.", nameof(id));
        }

        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("The project identifier must not be empty.", nameof(projectId));
        }

        if (updatedAt < createdAt)
        {
            throw new ArgumentException("The update timestamp must not precede creation.", nameof(updatedAt));
        }

        Id = id;
        ProjectId = projectId;
        Number = number;
        Title = ValidateTitle(title);
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public Guid Id { get; }

    public Guid ProjectId { get; }

    public ChapterNumber Number { get; }

    public string? Title { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    private static string? ValidateTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string normalizedTitle = title.Trim();
        if (normalizedTitle.Length > MaximumTitleLength || normalizedTitle.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"The chapter title must contain at most {MaximumTitleLength} characters and no control characters.",
                nameof(title));
        }

        return normalizedTitle;
    }
}
