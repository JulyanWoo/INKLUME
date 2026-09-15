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

        if (string.Equals(sourcePath, normalizedProjectRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidSource,
                "Select a chapter folder or images folder, not the project root.");
        }

        if (ProjectPaths.IsDescendantOf(normalizedProjectRoot, sourcePath))
        {
            string contextPath = Path.Combine(normalizedProjectRoot, "context");
            string cachePath = Path.Combine(normalizedProjectRoot, "cache");

            if (string.Equals(sourcePath, contextPath, StringComparison.OrdinalIgnoreCase)
                || ProjectPaths.IsDescendantOf(contextPath, sourcePath)
                || string.Equals(sourcePath, cachePath, StringComparison.OrdinalIgnoreCase)
                || ProjectPaths.IsDescendantOf(cachePath, sourcePath))
            {
                throw new ProjectOperationException(ProjectErrorCode.InvalidSource,
                    "Reserved internal project folders cannot be used as a chapter source.");
            }

            string folderName = Path.GetFileName(sourcePath);
            if (folderName.EndsWith("_raw", StringComparison.OrdinalIgnoreCase))
            {
                throw new ProjectOperationException(ProjectErrorCode.InvalidSource,
                    "An active chapter RAW folder cannot be imported as a new chapter.");
            }
        }

        string[] files = [.. Directory.EnumerateFiles(sourcePath, "*", SearchOption.TopDirectoryOnly)];
        ChapterSourceImage[] images = [.. files
            .Where(path => SupportedImageFormats.IsSupported(path))
            .OrderBy(path => Path.GetFileName(path), NaturalFileNameComparer.Instance)
            .Select(path => new ChapterSourceImage(
                Path.GetFileName(path),
                Path.GetExtension(path).ToLowerInvariant(),
                token => OpenReadAsync(path, token)))];
        string[] ignoredFiles = [.. files
            .Where(path => !SupportedImageFormats.IsSupported(path))
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name, NaturalFileNameComparer.Instance)];
        if (images.Length == 0)
        {
            throw new ProjectOperationException(ProjectErrorCode.NoSupportedImages,
                $"The source folder contains no supported {SupportedImageFormats.DisplayDescription} images.");
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
