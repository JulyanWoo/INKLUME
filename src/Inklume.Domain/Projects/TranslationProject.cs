namespace Inklume.Domain.Projects;

public sealed record TranslationProject
{
    public const int MaximumNameLength = 200;

    public TranslationProject(
        Guid id,
        string name,
        string seriesName,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The project identifier must not be empty.", nameof(id));
        }

        if (updatedAt < createdAt)
        {
            throw new ArgumentException("The update timestamp must not precede creation.", nameof(updatedAt));
        }

        Id = id;
        Name = ValidateName(name, nameof(name));
        SeriesName = ValidateName(seriesName, nameof(seriesName));
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public Guid Id { get; }

    public string Name { get; }

    public string SeriesName { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    private static string ValidateName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("Names must not contain control characters.", parameterName);
        }

        string normalizedName = value.Trim();
        if (normalizedName.Length > MaximumNameLength)
        {
            throw new ArgumentException($"Names must not exceed {MaximumNameLength} characters.", parameterName);
        }

        return normalizedName;
    }
}
