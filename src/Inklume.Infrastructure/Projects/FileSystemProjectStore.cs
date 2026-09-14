using System.Text.Json;
using Inklume.Application.Projects;
using Inklume.Domain.Projects;
using Inklume.Infrastructure.Chapters;
using Inklume.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inklume.Infrastructure.Projects;

public sealed class FileSystemProjectStore : IProjectStore
{
    public async Task<ProjectWorkspace> CreateAsync(
        TranslationProject project, string rootPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string normalizedPath = ProjectPaths.Normalize(rootPath);
            await RejectOccupiedDirectoryAsync(normalizedPath, cancellationToken);
            string? parentPath = Path.GetDirectoryName(normalizedPath);
            if (parentPath is null || !Directory.Exists(parentPath))
            {
                throw new ProjectOperationException(ProjectErrorCode.InvalidPath,
                    "The parent folder must exist before creating a project.");
            }

            Directory.CreateDirectory(normalizedPath);
            using FileStream creationLock = AcquireCreationLock(normalizedPath);
            if (Directory.EnumerateFileSystemEntries(normalizedPath)
                .Any(path => !string.Equals(Path.GetFileName(path), ProjectPaths.CreationLockFileName, StringComparison.Ordinal)))
            {
                throw new ProjectOperationException(ProjectErrorCode.DirectoryNotEmpty,
                    "The selected folder is no longer empty. Its contents were not replaced.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(normalizedPath, "chapters"));
            Directory.CreateDirectory(Path.Combine(normalizedPath, "cache"));
            string databasePath = Path.Combine(normalizedPath, ProjectPaths.DatabaseFileName);
            await SqliteProjectPersistence.InitializeAsync(databasePath, cancellationToken);
            await ProjectContextFiles.CreateAsync(normalizedPath, project, cancellationToken);
            // Metadata is committed last, so interrupted creation cannot masquerade as a complete project.
            await SqliteProjectPersistence.SaveAsync(databasePath, project, cancellationToken);
            return new ProjectWorkspace(project, normalizedPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ProjectOperationException(ProjectErrorCode.AccessDenied,
                "INKLUME cannot write to the selected project folder.", exception);
        }
        catch (Exception exception) when (exception is IOException or SqliteException or DbUpdateException)
        {
            throw new ProjectOperationException(ProjectErrorCode.StorageFailure,
                "The project could not be created. Any partial files were preserved; select an empty folder to retry.", exception);
        }
    }

    public async Task<ProjectWorkspace> OpenAsync(string rootPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string normalizedPath = ProjectPaths.Normalize(rootPath);
            if ((File.GetAttributes(normalizedPath) & FileAttributes.Directory) == 0)
            {
                throw new ProjectOperationException(ProjectErrorCode.InvalidPath, "The selected path is not a project folder.");
            }

            string databasePath = Path.Combine(normalizedPath, ProjectPaths.DatabaseFileName);
            if (!File.Exists(databasePath) && !Directory.Exists(Path.Combine(normalizedPath, "context")))
            {
                if (Directory.Exists(normalizedPath)
                    && Directory.EnumerateFiles(normalizedPath).Any(f => ImageFileValidator.SupportedExtensions.Contains(Path.GetExtension(f))))
                {
                    throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                        "This folder contains comic images, but is not an INKLUME project. Create a project first, then use 'Import Chapter' to import these images.");
                }

                throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                    "The selected folder is not an INKLUME project workspace. Choose a folder created with 'New Project'.");
            }

            foreach (string directoryName in new[] { "context", "chapters", "cache" })
            {
                ProjectPaths.RequireDirectory(Path.Combine(normalizedPath, directoryName));
            }

            ProjectPaths.RequireFile(databasePath);
            TranslationProject project = await SqliteProjectPersistence.ReadAsync(databasePath, cancellationToken);
            await ProjectContextFiles.ValidateAsync(normalizedPath, project, cancellationToken);
            return new ProjectWorkspace(project, normalizedPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ProjectOperationException(ProjectErrorCode.AccessDenied,
                "INKLUME cannot read the selected project folder.", exception);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ProjectOperationException(ProjectErrorCode.NotFound,
                "The selected project folder or one of its files no longer exists.", exception);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 3 or 8 or 14)
        {
            throw new ProjectOperationException(ProjectErrorCode.AccessDenied,
                "The project database cannot be accessed. Check its permissions and availability.", exception);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            throw new ProjectOperationException(ProjectErrorCode.StorageFailure,
                "The project database is busy. Close the other operation and try again.", exception);
        }
        catch (Exception exception) when (exception is JsonException or SqliteException or FormatException
            or InvalidOperationException or OverflowException or InvalidCastException)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "The selected folder contains invalid or incomplete INKLUME project data.", exception);
        }
        catch (IOException exception)
        {
            throw new ProjectOperationException(ProjectErrorCode.StorageFailure,
                "The project files could not be read. Check folder access and try again.", exception);
        }
    }

    private async Task RejectOccupiedDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidPath, "The selected path is a file, not a folder.");
        }

        if (!Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any())
        {
            return;
        }

        if (File.Exists(Path.Combine(path, ProjectPaths.CreationLockFileName)))
        {
            throw new ProjectOperationException(ProjectErrorCode.AlreadyExists,
                "Another operation is already creating a project in this folder.");
        }

        try
        {
            await OpenAsync(path, cancellationToken);
        }
        catch (ProjectOperationException exception)
        {
            throw new ProjectOperationException(ProjectErrorCode.DirectoryNotEmpty,
                "Choose an empty folder. Existing files were not replaced.", exception);
        }

        throw new ProjectOperationException(ProjectErrorCode.AlreadyExists,
            "This folder already contains an INKLUME project. Use Open project instead.");
    }

    private static FileStream AcquireCreationLock(string rootPath)
    {
        string lockPath = Path.Combine(rootPath, ProjectPaths.CreationLockFileName);
        try
        {
            return new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1, FileOptions.DeleteOnClose);
        }
        catch (IOException exception) when (File.Exists(lockPath))
        {
            throw new ProjectOperationException(ProjectErrorCode.AlreadyExists,
                "Another operation is already creating a project in this folder.", exception);
        }
    }
}
