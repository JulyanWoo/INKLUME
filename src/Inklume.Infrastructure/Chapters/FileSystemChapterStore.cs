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
        string? createdRawPath = null;
        bool finalPathOwned = false;

        try
        {
            ProjectWorkspace verifiedWorkspace = await VerifyWorkspaceAsync(workspace, cancellationToken);
            string chapterFolderName = chapter.Number.ToFolderName();
            string chaptersPath = Path.Combine(verifiedWorkspace.SourceRoot, "chapters");
            finalChapterPath = Path.Combine(chaptersPath, chapterFolderName);
            string rawFolderName = $"{chapterFolderName}_raw";
            bool chapterFolderPreExisted = await CheckDuplicateAndPreExistingAsync(
                verifiedWorkspace, chapter, finalChapterPath, rawFolderName, cancellationToken);

            stagingPath = Path.Combine(chaptersPath, $".import-{chapterFolderName}-{Guid.NewGuid():N}");
            string stagingRawPath = Path.Combine(stagingPath, rawFolderName);
            Directory.CreateDirectory(stagingRawPath);
            IReadOnlyList<Page> pages = await CopyImagesAsync(
                source.Images, stagingRawPath, chapterFolderName, rawFolderName,
                chapter.Id, progress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (chapterFolderPreExisted)
            {
                string targetRawPath = Path.Combine(finalChapterPath, rawFolderName);
                Directory.Move(stagingRawPath, targetRawPath);
                createdRawPath = targetRawPath;
                CleanupOwnedDirectory(stagingPath);
                stagingPath = null;
            }
            else
            {
                Directory.Move(stagingPath, finalChapterPath);
                stagingPath = null;
                finalPathOwned = true;
            }

            ProjectWorkspace updatedWorkspace = await PersistAsync(verifiedWorkspace, chapter, pages, cancellationToken);
            finalPathOwned = false;
            createdRawPath = null;

            IReadOnlyList<PageWorkspace> pageWorkspaces = pages
                .Select(page => new PageWorkspace(page, ResolvePagePath(updatedWorkspace.SourceRoot, page.RelativePath)))
                .ToArray();
            return new ChapterImportResult(updatedWorkspace, chapter, pageWorkspaces, source.IgnoredFiles);
        }
        catch (OperationCanceledException)
        {
            CleanupOwnedDirectory(stagingPath);
            if (createdRawPath is not null)
            {
                CleanupOwnedDirectory(createdRawPath);
            }

            if (finalPathOwned)
            {
                CleanupOwnedDirectory(finalChapterPath);
            }

            throw;
        }
        catch (ProjectOperationException)
        {
            CleanupOwnedDirectory(stagingPath);
            if (createdRawPath is not null)
            {
                CleanupOwnedDirectory(createdRawPath);
            }

            if (finalPathOwned)
            {
                CleanupOwnedDirectory(finalChapterPath);
            }

            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            CleanupOwnedDirectory(stagingPath);
            if (createdRawPath is not null)
            {
                CleanupOwnedDirectory(createdRawPath);
            }

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
            if (createdRawPath is not null)
            {
                CleanupOwnedDirectory(createdRawPath);
            }

            if (finalPathOwned)
            {
                CleanupOwnedDirectory(finalChapterPath);
            }

            throw new ProjectOperationException(ProjectErrorCode.ImportFailure,
                "The chapter import failed. No partial chapter was registered and the source images were preserved.", exception);
        }
    }

    public async Task<ChapterIndexResult> IndexChapterInPlaceAsync(
        ProjectWorkspace workspace,
        string chapterDirectoryPath,
        ChapterNumber chapterNumber,
        string? title,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterDirectoryPath);
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedChapterDir = Path.GetFullPath(chapterDirectoryPath);
        if (!Directory.Exists(normalizedChapterDir))
        {
            throw new ProjectOperationException(ProjectErrorCode.NotFound,
                $"The chapter directory does not exist: {chapterDirectoryPath}");
        }

        if (!IsSameOrDescendant(normalizedChapterDir, workspace.SourceRoot))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidPath,
                "The chapter directory must be within the project source folder.");
        }

        string databasePath = workspace.DatabasePath;
        await using ProjectDbContext context = SqliteProjectPersistence.CreateContext(databasePath, readOnly: false);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);

        ChapterMetadata? existing = await context.Chapters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProjectId == workspace.Project.Id && c.Number == chapterNumber.Value, cancellationToken);

        if (existing is not null)
        {
            Chapter existingChapter = ToDomainChapter(existing);
            IReadOnlyList<PageWorkspace> existingPages = await GetPagesAsync(workspace, existingChapter.Id, cancellationToken);
            return new ChapterIndexResult(workspace, existingChapter, existingPages);
        }

        List<string> imageFiles = [.. Directory.EnumerateFiles(normalizedChapterDir)
            .Where(SupportedImageFormats.IsSupported)
            .OrderBy(f => Path.GetFileName(f) ?? string.Empty, NaturalFileNameComparer.Instance)];

        if (imageFiles.Count == 0)
        {
            throw new ProjectOperationException(ProjectErrorCode.NoSupportedImages,
                $"The chapter folder '{Path.GetFileName(normalizedChapterDir)}' contains no supported images.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid chapterId = Guid.NewGuid();
        var chapter = new Chapter(chapterId, workspace.Project.Id, chapterNumber, title, now, now);

        var pages = new List<Page>(imageFiles.Count);
        for (int i = 0; i < imageFiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string imagePath = imageFiles[i];
            string fileName = Path.GetFileName(imagePath);
            string extension = Path.GetExtension(imagePath);
            int pageNumber = i + 1;

            string relativePath = Path.GetRelativePath(workspace.SourceRoot, imagePath);

            string hash;
            await using (var hashStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] hashBuffer = ArrayPool<byte>.Shared.Rent(81920);
                try
                {
                    int bytesRead;
                    while ((bytesRead = await hashStream.ReadAsync(hashBuffer.AsMemory(0, hashBuffer.Length), cancellationToken)) > 0)
                    {
                        sha256.AppendData(hashBuffer, 0, bytesRead);
                    }

                    hash = Convert.ToHexString(sha256.GetHashAndReset());
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(hashBuffer);
                }
            }

            await using var validationStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            ImageDimensions dimensions = await ImageFileValidator.ValidateAsync(validationStream, fileName, extension, cancellationToken);

            pages.Add(new Page(
                Guid.NewGuid(),
                chapterId,
                pageNumber,
                fileName,
                relativePath,
                hash,
                dimensions.PixelWidth,
                dimensions.PixelHeight));
        }

        context.Chapters.Add(new ChapterMetadata
        {
            Id = chapter.Id,
            ProjectId = chapter.ProjectId,
            Number = chapter.Number.Value,
            Title = chapter.Title,
            CreatedAt = chapter.CreatedAt,
            UpdatedAt = chapter.UpdatedAt
        });

        context.Pages.AddRange(pages.Select(p => new PageMetadata
        {
            Id = p.Id,
            ChapterId = p.ChapterId,
            Number = p.Number,
            OriginalFileName = p.OriginalFileName,
            RelativePath = p.RelativePath,
            ContentHash = p.ContentHash,
            PixelWidth = p.PixelWidth,
            PixelHeight = p.PixelHeight
        }));

        ProjectMetadata projectMetadata = await context.Projects.SingleAsync(
            metadata => metadata.Id == workspace.Project.Id, cancellationToken);
        projectMetadata.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);

        var updatedProject = new TranslationProject(
            workspace.Project.Id, workspace.Project.Name, workspace.Project.SeriesName,
            workspace.Project.CreatedAt, now);
        var updatedWorkspace = new ProjectWorkspace(updatedProject, workspace.SourceRoot, workspace.DataRoot);

        IReadOnlyList<PageWorkspace> pageWorkspaces = [.. pages.Select(p => new PageWorkspace(p, ResolvePagePath(workspace.SourceRoot, p.RelativePath)))];
        return new ChapterIndexResult(updatedWorkspace, chapter, pageWorkspaces);
    }

    public async Task<IReadOnlyList<Chapter>> GetChaptersAsync(
        ProjectWorkspace workspace,
        CancellationToken cancellationToken)
    {
        ProjectWorkspace verifiedWorkspace = await VerifyWorkspaceAsync(workspace, cancellationToken);
        string databasePath = verifiedWorkspace.DatabasePath;
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
        string databasePath = verifiedWorkspace.DatabasePath;
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
            return new PageWorkspace(page, ResolvePagePath(verifiedWorkspace.SourceRoot, page.RelativePath));
        }).ToArray();
    }

    public async Task<PageWorkspace> EnsurePageDimensionsAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        ProjectWorkspace verifiedWorkspace = await VerifyWorkspaceAsync(workspace, cancellationToken);
        string databasePath = verifiedWorkspace.DatabasePath;
        await using ProjectDbContext context = SqliteProjectPersistence.CreateContext(databasePath, readOnly: false);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);
        PageMetadata? metadata = await context.Pages
            .Join(
                context.Chapters.Where(chapter => chapter.ProjectId == verifiedWorkspace.Project.Id),
                page => page.ChapterId,
                chapter => chapter.Id,
                (page, chapter) => page)
            .SingleOrDefaultAsync(page => page.Id == pageId, cancellationToken);
        if (metadata is null)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "The selected page does not belong to the open project.");
        }

        Page page = ToDomainPage(metadata);
        string filePath = ResolvePagePath(verifiedWorkspace.SourceRoot, page.RelativePath);
        if (!page.HasDimensions)
        {
            string extension = Path.GetExtension(filePath);
            await using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.RandomAccess);
            ImageDimensions dimensions = await ImageFileValidator.ValidateAsync(
                stream, page.OriginalFileName, extension, cancellationToken);
            page = page.WithDimensions(dimensions.PixelWidth, dimensions.PixelHeight);
            metadata.PixelWidth = dimensions.PixelWidth;
            metadata.PixelHeight = dimensions.PixelHeight;
            await context.SaveChangesAsync(cancellationToken);
        }

        return new PageWorkspace(page, filePath);
    }

    private static async Task<ProjectWorkspace> VerifyWorkspaceAsync(
        ProjectWorkspace workspace, CancellationToken cancellationToken)
    {
        string databasePath = workspace.DatabasePath;
        if (!File.Exists(databasePath))
        {
            throw new ProjectOperationException(ProjectErrorCode.NotFound,
                "The workspace database could not be found.");
        }

        TranslationProject project = await SqliteProjectPersistence.ReadAsync(databasePath, cancellationToken);
        if (project.Id != workspace.Project.Id)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "The selected workspace no longer matches the open project.");
        }

        return workspace with { Project = project };
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
                    ImageDimensions dimensions = await ImageFileValidator.ValidateAsync(
                        validationStream, sourceImage.OriginalFileName, extension, cancellationToken);

                    string relativePath = Path.Combine("chapters", chapterFolderName, rawFolderName, destinationFileName);
                    pages.Add(new Page(Guid.NewGuid(), chapterId, pageNumber,
                        sourceImage.OriginalFileName, relativePath, hash,
                        dimensions.PixelWidth, dimensions.PixelHeight));
                }

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

    private static async Task<bool> CheckDuplicateAndPreExistingAsync(
        ProjectWorkspace workspace, Chapter chapter, string finalChapterPath, string rawFolderName, CancellationToken cancellationToken)
    {
        string databasePath = workspace.DatabasePath;
        await using ProjectDbContext context = SqliteProjectPersistence.CreateContext(databasePath, readOnly: true);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);
        if (await context.Chapters.AsNoTracking().AnyAsync(
            metadata => metadata.ProjectId == workspace.Project.Id && metadata.Number == chapter.Number.Value,
            cancellationToken))
        {
            throw new ProjectOperationException(ProjectErrorCode.DuplicateChapter,
                $"Chapter {chapter.Number} is already imported.");
        }

        if (File.Exists(finalChapterPath))
        {
            throw new ProjectOperationException(ProjectErrorCode.DuplicateChapter,
                $"Chapter {chapter.Number} conflicts with an existing file in chapters.");
        }

        if (Directory.Exists(finalChapterPath))
        {
            string existingRawPath = Path.Combine(finalChapterPath, rawFolderName);
            if (Directory.Exists(existingRawPath))
            {
                throw new ProjectOperationException(ProjectErrorCode.DuplicateChapter,
                    $"Chapter {chapter.Number} already has a raw images folder in this project.");
            }

            return true;
        }

        return false;
    }

    private static async Task<ProjectWorkspace> PersistAsync(
        ProjectWorkspace workspace, Chapter chapter, IReadOnlyList<Page> pages, CancellationToken cancellationToken)
    {
        string databasePath = workspace.DatabasePath;
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
            ContentHash = page.ContentHash,
            PixelWidth = page.PixelWidth,
            PixelHeight = page.PixelHeight
        }));

        DateTimeOffset updatedAt = chapter.UpdatedAt > projectMetadata.UpdatedAt
            ? chapter.UpdatedAt
            : projectMetadata.UpdatedAt;
        projectMetadata.UpdatedAt = updatedAt;
        await context.SaveChangesAsync(cancellationToken);
        var updatedProject = new TranslationProject(
            workspace.Project.Id, workspace.Project.Name, workspace.Project.SeriesName,
            workspace.Project.CreatedAt, updatedAt);
        return new ProjectWorkspace(updatedProject, workspace.SourceRoot, workspace.DataRoot);
    }

    private static Chapter ToDomainChapter(ChapterMetadata metadata)
        => new(metadata.Id, metadata.ProjectId, new ChapterNumber(metadata.Number), metadata.Title,
            metadata.CreatedAt, metadata.UpdatedAt);

    private static Page ToDomainPage(PageMetadata metadata)
        => new(metadata.Id, metadata.ChapterId, metadata.Number, metadata.OriginalFileName,
            metadata.RelativePath, metadata.ContentHash, metadata.PixelWidth, metadata.PixelHeight);

    private static string ResolvePagePath(string sourceRoot, string relativePath)
    {
        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "A page contains an absolute path instead of a project-relative path.");
        }

        string resolved = Path.GetFullPath(Path.Combine(sourceRoot, relativePath));
        if (!IsSameOrDescendant(resolved, sourceRoot) || string.Equals(resolved, sourceRoot, StringComparison.OrdinalIgnoreCase))
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
