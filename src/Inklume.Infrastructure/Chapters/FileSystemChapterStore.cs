using System.Buffers;
using System.Security.Cryptography;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Domain.Projects;
using Inklume.Infrastructure.Persistence;
using Inklume.Infrastructure.Projects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inklume.Infrastructure.Chapters;

public sealed class FileSystemChapterStore : IChapterStore
{
    public async Task<ChapterImportResult> ImportAsync(
        ProjectWorkspace workspace,
        Chapter chapter,
        ChapterSource source,
        IProgress<ChapterImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(chapter);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        string? stagingPath = null;
        string? finalChapterPath = null;
        bool finalPathOwned = false;

        try
        {
            ProjectWorkspace verifiedWorkspace = await VerifyWorkspaceAsync(workspace, cancellationToken);
            string chapterFolderName = chapter.Number.ToFolderName();
            string chaptersPath = Path.Combine(verifiedWorkspace.RootPath, "chapters");
            finalChapterPath = Path.Combine(chaptersPath, chapterFolderName);
            await RejectDuplicateAsync(verifiedWorkspace, chapter, finalChapterPath, cancellationToken);

            stagingPath = Path.Combine(chaptersPath, $".import-{chapterFolderName}-{Guid.NewGuid():N}");
            string rawFolderName = $"{chapterFolderName}_raw";
            string stagingRawPath = Path.Combine(stagingPath, rawFolderName);
            Directory.CreateDirectory(stagingRawPath);
            IReadOnlyList<Page> pages = await CopyImagesAsync(
                source.Images, stagingRawPath, chapterFolderName, rawFolderName,
                chapter.Id, progress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stagingPath, finalChapterPath);
            stagingPath = null;
            finalPathOwned = true;
            ProjectWorkspace updatedWorkspace = await PersistAsync(verifiedWorkspace, chapter, pages, cancellationToken);
            finalPathOwned = false;

            IReadOnlyList<PageWorkspace> pageWorkspaces = pages
                .Select(page => new PageWorkspace(page, ResolvePagePath(updatedWorkspace.RootPath, page.RelativePath)))
                .ToArray();
            return new ChapterImportResult(updatedWorkspace, chapter, pageWorkspaces, source.IgnoredFiles);
        }
        catch (OperationCanceledException)
        {
            CleanupOwnedDirectory(stagingPath);
            if (finalPathOwned)
            {
                CleanupOwnedDirectory(finalChapterPath);
            }

            throw;
        }
        catch (ProjectOperationException)
        {
            CleanupOwnedDirectory(stagingPath);
            if (finalPathOwned)
            {
                CleanupOwnedDirectory(finalChapterPath);
            }

            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            CleanupOwnedDirectory(stagingPath);
            if (finalPathOwned)
            {
                CleanupOwnedDirectory(finalChapterPath);
            }

            throw new ProjectOperationException(ProjectErrorCode.AccessDenied,
                "INKLUME cannot read the source images or write the chapter folder.", exception);
        }
        catch (Exception exception) when (exception is IOException or CryptographicException
            or SqliteException or DbUpdateException)
        {
            CleanupOwnedDirectory(stagingPath);
            if (finalPathOwned)
            {
                CleanupOwnedDirectory(finalChapterPath);
            }

            throw new ProjectOperationException(ProjectErrorCode.ImportFailure,
                "The chapter import failed. No partial chapter was registered and the source images were preserved.", exception);
        }
    }

    public async Task<IReadOnlyList<Chapter>> GetChaptersAsync(
        ProjectWorkspace workspace,
        CancellationToken cancellationToken)
    {
        ProjectWorkspace verifiedWorkspace = await VerifyWorkspaceAsync(workspace, cancellationToken);
        string databasePath = Path.Combine(verifiedWorkspace.RootPath, ProjectPaths.DatabaseFileName);
        await using ProjectDbContext context = SqliteProjectPersistence.CreateContext(databasePath, readOnly: true);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);
        List<ChapterMetadata> records = await context.Chapters.AsNoTracking()
            .Where(metadata => metadata.ProjectId == verifiedWorkspace.Project.Id)
            .ToListAsync(cancellationToken);

        return records.Select(ToDomainChapter).OrderBy(chapter => chapter.Number).ToArray();
    }

    public async Task<IReadOnlyList<PageWorkspace>> GetPagesAsync(
        ProjectWorkspace workspace,
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        ProjectWorkspace verifiedWorkspace = await VerifyWorkspaceAsync(workspace, cancellationToken);
        string databasePath = Path.Combine(verifiedWorkspace.RootPath, ProjectPaths.DatabaseFileName);
        await using ProjectDbContext context = SqliteProjectPersistence.CreateContext(databasePath, readOnly: true);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);
        bool chapterBelongsToProject = await context.Chapters.AsNoTracking()
            .AnyAsync(metadata => metadata.Id == chapterId && metadata.ProjectId == workspace.Project.Id, cancellationToken);
        if (!chapterBelongsToProject)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "The selected chapter does not belong to the open project.");
        }

        List<PageMetadata> records = await context.Pages.AsNoTracking()
            .Where(metadata => metadata.ChapterId == chapterId)
            .OrderBy(metadata => metadata.Number)
            .ToListAsync(cancellationToken);
        return records.Select(metadata =>
        {
            Page page = ToDomainPage(metadata);
            return new PageWorkspace(page, ResolvePagePath(verifiedWorkspace.RootPath, page.RelativePath));
        }).ToArray();
    }

    private static async Task<ProjectWorkspace> VerifyWorkspaceAsync(
        ProjectWorkspace workspace, CancellationToken cancellationToken)
    {
        ProjectWorkspace verified = await new FileSystemProjectStore().OpenAsync(workspace.RootPath, cancellationToken);
        if (verified.Project.Id != workspace.Project.Id)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "The selected workspace no longer matches the open project.");
        }

        return verified;
    }

    private static async Task<IReadOnlyList<Page>> CopyImagesAsync(
        IReadOnlyList<ChapterSourceImage> sourceImages,
        string stagingRawPath,
        string chapterFolderName,
        string rawFolderName,
        Guid chapterId,
        IProgress<ChapterImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        int padding = Math.Max(3, sourceImages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        var pages = new List<Page>(sourceImages.Count);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            for (int index = 0; index < sourceImages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ChapterSourceImage sourceImage = sourceImages[index];
                int pageNumber = index + 1;
                string extension = sourceImage.FileExtension.ToLowerInvariant();
                if (!ImageFileValidator.SupportedExtensions.Contains(extension))
                {
                    throw new ProjectOperationException(ProjectErrorCode.InvalidImage,
                        $"The chapter source contains an unsupported image: {sourceImage.OriginalFileName}.");
                }

                string destinationFileName = string.Concat(
                    pageNumber.ToString($"D{padding}", System.Globalization.CultureInfo.InvariantCulture), extension);
                string destinationPath = Path.Combine(stagingRawPath, destinationFileName);
                progress?.Report(new ChapterImportProgress(index, sourceImages.Count, sourceImage.OriginalFileName));
                await using Stream sourceStream = await sourceImage.OpenReadAsync(cancellationToken);
                if (!sourceStream.CanRead)
                {
                    throw new ProjectOperationException(ProjectErrorCode.InvalidImage,
                        $"The image source is not readable: {sourceImage.OriginalFileName}.");
                }

                string hash = await CopyAndHashAsync(sourceStream, destinationPath, buffer, cancellationToken);
                await using (var validationStream = new FileStream(
                    destinationPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await ImageFileValidator.ValidateAsync(
                        validationStream, sourceImage.OriginalFileName, extension, cancellationToken);
                }

                string relativePath = Path.Combine("chapters", chapterFolderName, rawFolderName, destinationFileName);
                pages.Add(new Page(Guid.NewGuid(), chapterId, pageNumber,
                    sourceImage.OriginalFileName, relativePath, hash));
                progress?.Report(new ChapterImportProgress(pageNumber, sourceImages.Count, sourceImage.OriginalFileName));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return pages;
    }

    private static async Task<string> CopyAndHashAsync(
        Stream source, string destinationPath, byte[] buffer, CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hash.AppendData(buffer, 0, read);
        }

        await destination.FlushAsync(cancellationToken);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task RejectDuplicateAsync(
        ProjectWorkspace workspace, Chapter chapter, string finalChapterPath, CancellationToken cancellationToken)
    {
        if (Directory.Exists(finalChapterPath) || File.Exists(finalChapterPath))
        {
            throw new ProjectOperationException(ProjectErrorCode.DuplicateChapter,
                $"Chapter {chapter.Number} already has a folder in this project.");
        }

        string databasePath = Path.Combine(workspace.RootPath, ProjectPaths.DatabaseFileName);
        await using ProjectDbContext context = SqliteProjectPersistence.CreateContext(databasePath, readOnly: true);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);
        if (await context.Chapters.AsNoTracking().AnyAsync(
            metadata => metadata.ProjectId == workspace.Project.Id && metadata.Number == chapter.Number.Value,
            cancellationToken))
        {
            throw new ProjectOperationException(ProjectErrorCode.DuplicateChapter,
                $"Chapter {chapter.Number} is already imported.");
        }
    }

    private static async Task<ProjectWorkspace> PersistAsync(
        ProjectWorkspace workspace, Chapter chapter, IReadOnlyList<Page> pages, CancellationToken cancellationToken)
    {
        string databasePath = Path.Combine(workspace.RootPath, ProjectPaths.DatabaseFileName);
        await using ProjectDbContext context = SqliteProjectPersistence.CreateContext(databasePath, readOnly: false);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);
        ProjectMetadata projectMetadata = await context.Projects.SingleAsync(
            metadata => metadata.Id == workspace.Project.Id, cancellationToken);
        context.Chapters.Add(new ChapterMetadata
        {
            Id = chapter.Id,
            ProjectId = chapter.ProjectId,
            Number = chapter.Number.Value,
            Title = chapter.Title,
            CreatedAt = chapter.CreatedAt,
            UpdatedAt = chapter.UpdatedAt
        });
        context.Pages.AddRange(pages.Select(page => new PageMetadata
        {
            Id = page.Id,
            ChapterId = page.ChapterId,
            Number = page.Number,
            OriginalFileName = page.OriginalFileName,
            RelativePath = page.RelativePath,
            ContentHash = page.ContentHash
        }));

        DateTimeOffset updatedAt = chapter.UpdatedAt > projectMetadata.UpdatedAt
            ? chapter.UpdatedAt
            : projectMetadata.UpdatedAt;
        projectMetadata.UpdatedAt = updatedAt;
        await context.SaveChangesAsync(cancellationToken);
        var updatedProject = new TranslationProject(
            workspace.Project.Id, workspace.Project.Name, workspace.Project.SeriesName,
            workspace.Project.CreatedAt, updatedAt);
        return new ProjectWorkspace(updatedProject, workspace.RootPath);
    }

    private static Chapter ToDomainChapter(ChapterMetadata metadata)
        => new(metadata.Id, metadata.ProjectId, new ChapterNumber(metadata.Number), metadata.Title,
            metadata.CreatedAt, metadata.UpdatedAt);

    private static Page ToDomainPage(PageMetadata metadata)
        => new(metadata.Id, metadata.ChapterId, metadata.Number, metadata.OriginalFileName,
            metadata.RelativePath, metadata.ContentHash);

    private static string ResolvePagePath(string projectRoot, string relativePath)
    {
        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "A page contains an absolute path instead of a project-relative path.");
        }

        string resolved = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        if (!IsSameOrDescendant(resolved, projectRoot) || string.Equals(resolved, projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "A page path points outside the open project.");
        }

        ProjectPaths.RequireFile(resolved);
        return resolved;
    }

    private static bool IsSameOrDescendant(string candidate, string parent)
    {
        string relative = Path.GetRelativePath(parent, candidate);
        return relative == "." || (!Path.IsPathRooted(relative)
            && !relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment == ".."));
    }

    private static void CleanupOwnedDirectory(string? path)
    {
        if (path is null || !Directory.Exists(path))
        {
            return;
        }

        ProjectPaths.RejectReparsePoints(path);
        VerifyNoReparsePoints(new DirectoryInfo(path));
        Directory.Delete(path, recursive: true);
    }

    private static void VerifyNoReparsePoints(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ProjectOperationException(ProjectErrorCode.ImportFailure,
                "The incomplete import could not be cleaned safely because it contains a reparse point.");
        }

        if (entry is DirectoryInfo directory)
        {
            foreach (FileSystemInfo child in directory.EnumerateFileSystemInfos())
            {
                VerifyNoReparsePoints(child);
            }
        }
    }
}
