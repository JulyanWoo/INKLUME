using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Inklume.Application.Projects;
using Inklume.Domain.Projects;

namespace Inklume.Infrastructure.Projects;

internal static class ProjectContextFiles
{
    internal const int FormatVersion = 1;
    private const long MaximumContextFileBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    internal static async Task CreateAsync(string rootPath, TranslationProject project, CancellationToken cancellationToken)
    {
        string contextPath = Path.Combine(rootPath, "context");
        Directory.CreateDirectory(contextPath);
        var header = new ProjectContextHeader(FormatVersion, project.Id);

        await WriteAsync(Path.Combine(contextPath, "series.json"),
            new SeriesContextDocument(FormatVersion, project.Id, project.SeriesName), cancellationToken);
        await WriteAsync(Path.Combine(contextPath, "characters.json"), header, cancellationToken);
        await WriteAsync(Path.Combine(contextPath, "glossary.json"), header, cancellationToken);
        await WriteAsync(Path.Combine(contextPath, "translation_rules.json"), header, cancellationToken);
    }

    internal static async Task ValidateAsync(string rootPath, TranslationProject project, CancellationToken cancellationToken)
    {
        string contextPath = Path.Combine(rootPath, "context");
        SeriesContextDocument series = await ReadAsync<SeriesContextDocument>(Path.Combine(contextPath, "series.json"), cancellationToken);
        ValidateHeader(series.FormatVersion, series.ProjectId, project.Id);
        if (!string.Equals(series.Name, project.SeriesName, StringComparison.Ordinal))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "The series context does not match the project metadata.");
        }

        foreach (string fileName in new[] { "characters.json", "glossary.json", "translation_rules.json" })
        {
            ProjectContextHeader header = await ReadAsync<ProjectContextHeader>(Path.Combine(contextPath, fileName), cancellationToken);
            ValidateHeader(header.FormatVersion, header.ProjectId, project.Id);
        }
    }

    private static async Task WriteAsync<T>(string path, T document, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        ProjectPaths.RequireFile(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous);
        if (stream.Length > MaximumContextFileBytes)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                $"The context file exceeds the supported size: {Path.GetFileName(path)}.");
        }

        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                $"The context file contains no document: {Path.GetFileName(path)}.");
    }

    private static void ValidateHeader(int version, Guid projectId, Guid expectedProjectId)
    {
        if (version != FormatVersion)
        {
            throw new ProjectOperationException(ProjectErrorCode.IncompatibleVersion,
                "The project context format is not supported by this version of INKLUME.");
        }

        if (projectId != expectedProjectId)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "A context file belongs to a different project.");
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private sealed record ProjectContextHeader(int FormatVersion, Guid ProjectId);

    private sealed record SeriesContextDocument(int FormatVersion, Guid ProjectId, string Name);
}
