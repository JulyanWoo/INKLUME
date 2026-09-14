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

        string normalizedPath = ProjectPaths.Normalize(rootPath);
        if (File.Exists(normalizedPath))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidPath, "The selected path is a file, not a folder.");
        }

        string? parentPath = Path.GetDirectoryName(normalizedPath);
        if (parentPath is null || !Directory.Exists(parentPath))
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidPath,
                "The parent folder must exist before creating a project.");
        }

        if (File.Exists(Path.Combine(normalizedPath, ProjectPaths.CreationLockFileName)))
        {
            throw new ProjectOperationException(ProjectErrorCode.AlreadyExists,
                "Another operation is already creating a project in this folder.");
        }

        string databasePath = Path.Combine(normalizedPath, ProjectPaths.DatabaseFileName);
        DatabaseProbeResult probeResult = await SqliteProjectPersistence.ProbeDatabaseAsync(databasePath, cancellationToken);
        if (probeResult == DatabaseProbeResult.ValidInklumeProject)
        {
            throw new ProjectOperationException(ProjectErrorCode.AlreadyExists,
                "This folder already contains an INKLUME project. Open the existing project instead.");
        }

        if (probeResult == DatabaseProbeResult.InvalidOrConflicting)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "A 'project.db' file already exists in this folder but is not a valid INKLUME project. INKLUME will not overwrite it.");
        }

        string contextPath = Path.Combine(normalizedPath, "context");
        if (Directory.Exists(contextPath))
        {
            string[] reservedContextFiles = ["series.json", "characters.json", "glossary.json", "translation_rules.json"];
            foreach (string fileName in reservedContextFiles)
            {
                if (File.Exists(Path.Combine(contextPath, fileName)))
                {
                    throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                        $"A reserved context file already exists ({fileName}) and conflicts with project initialization.");
                }
            }
        }

        var createdDirectories = new List<string>();
        var createdFiles = new List<string>();

        if (!Directory.Exists(normalizedPath))
        {
            Directory.CreateDirectory(normalizedPath);
            createdDirectories.Add(normalizedPath);
        }

        try
        {
            using FileStream creationLock = AcquireCreationLock(normalizedPath);
            cancellationToken.ThrowIfCancellationRequested();

            string chaptersPath = Path.Combine(normalizedPath, "chapters");
            if (!Directory.Exists(chaptersPath))
            {
                Directory.CreateDirectory(chaptersPath);
                createdDirectories.Add(chaptersPath);
            }

            string cachePath = Path.Combine(normalizedPath, "cache");
            if (!Directory.Exists(cachePath))
            {
                Directory.CreateDirectory(cachePath);
                createdDirectories.Add(cachePath);
            }

            if (!Directory.Exists(contextPath))
            {
                Directory.CreateDirectory(contextPath);
                createdDirectories.Add(contextPath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await SqliteProjectPersistence.InitializeAsync(databasePath, cancellationToken);
            createdFiles.Add(databasePath);

            await ProjectContextFiles.CreateAsync(normalizedPath, project, cancellationToken);
            createdFiles.Add(Path.Combine(contextPath, "series.json"));
            createdFiles.Add(Path.Combine(contextPath, "characters.json"));
            createdFiles.Add(Path.Combine(contextPath, "glossary.json"));
            createdFiles.Add(Path.Combine(contextPath, "translation_rules.json"));

            // Metadata is committed last, so interrupted creation cannot masquerade as a complete project.
            await SqliteProjectPersistence.SaveAsync(databasePath, project, cancellationToken);
            return new ProjectWorkspace(project, normalizedPath);
        }
        catch (Exception exception)
        {
            RollbackCreatedResources(createdFiles, createdDirectories);

            if (exception is ProjectOperationException or OperationCanceledException)
            {
                throw;
            }

            if (exception is UnauthorizedAccessException)
            {
                throw new ProjectOperationException(ProjectErrorCode.AccessDenied,
                    "INKLUME cannot write to the selected project folder.", exception);
            }

            throw new ProjectOperationException(ProjectErrorCode.StorageFailure,
                "The project could not be created. Pre-existing files were preserved.", exception);
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
                        "This folder contains comic images, but is not an INKLUME project.");
                }

                throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                    "The selected folder is not an INKLUME project workspace.");
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

    private static void RollbackCreatedResources(List<string> createdFiles, List<string> createdDirectories)
    {
        SqliteConnection.ClearAllPools();

        foreach (string filePath in createdFiles)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                string walPath = $"{filePath}-wal";
                if (File.Exists(walPath))
                {
                    File.Delete(walPath);
                }

                string shmPath = $"{filePath}-shm";
                if (File.Exists(shmPath))
                {
                    File.Delete(shmPath);
                }
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceWarning("Failed to delete created file during rollback: {0}", exception);
            }
        }

        for (int i = createdDirectories.Count - 1; i >= 0; i--)
        {
            string dirPath = createdDirectories[i];
            try
            {
                if (Directory.Exists(dirPath) && !Directory.EnumerateFileSystemEntries(dirPath).Any())
                {
                    Directory.Delete(dirPath);
                }
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceWarning("Failed to delete created directory during rollback: {0}", exception);
            }
        }
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
