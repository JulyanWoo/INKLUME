using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Infrastructure.Projects;

namespace Inklume.Infrastructure.Chapters;

public sealed class LocalFolderChapterSourceProvider : ILocalChapterSourceProvider
{
    public Task<ChapterSource> LoadAsync(
        string sourceFolder,
        string projectRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string sourcePath = ProjectPaths.Normalize(sourceFolder);
        string normalizedProjectRoot = ProjectPaths.Normalize(projectRoot);
        if (!Directory.Exists(sourcePath))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidSource, "The chapter source folder does not exist.");
        }

        if (IsSameOrDescendant(sourcePath, normalizedProjectRoot))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidSource,
                "Choose a source folder outside the INKLUME project.");
        }

        string[] files = [.. Directory.EnumerateFiles(sourcePath, "*", SearchOption.TopDirectoryOnly)];
        ChapterSourceImage[] images = [.. files
            .Where(path => ImageFileValidator.SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), NaturalFileNameComparer.Instance)
            .Select(path => new ChapterSourceImage(
                Path.GetFileName(path),
                Path.GetExtension(path).ToLowerInvariant(),
                token => OpenReadAsync(path, token)))];
        string[] ignoredFiles = [.. files
            .Where(path => !ImageFileValidator.SupportedExtensions.Contains(Path.GetExtension(path)))
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name, NaturalFileNameComparer.Instance)];
        if (images.Length == 0)
        {
            throw new ProjectOperationException(ProjectErrorCode.NoSupportedImages,
                "The source folder contains no supported PNG, JPEG, or WebP images.");
        }

        return Task.FromResult(new ChapterSource(images, ignoredFiles));
    }

    private static ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return ValueTask.FromResult(stream);
    }

    private static bool IsSameOrDescendant(string candidate, string parent)
    {
        string relative = Path.GetRelativePath(parent, candidate);
        return relative == "." || (!Path.IsPathRooted(relative)
            && !relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment == ".."));
    }
}
