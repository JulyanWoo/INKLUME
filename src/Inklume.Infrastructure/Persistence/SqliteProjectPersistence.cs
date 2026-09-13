using System.Data.Common;
using Inklume.Application.Projects;
using Inklume.Domain.Projects;
using Inklume.Infrastructure.Projects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inklume.Infrastructure.Persistence;

internal static class SqliteProjectPersistence
{
    // SQLite's application_id distinguishes this format from unrelated SQLite databases.
    internal const int ApplicationId = 0x494E4B4C;

    internal static async Task InitializeAsync(string databasePath, CancellationToken cancellationToken)
    {
        // Reserve the file without replacing an existing database, even after a race.
        await using (var reservation = new FileStream(databasePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await reservation.FlushAsync(cancellationToken);
        }

        await using ProjectDbContext context = CreateContext(databasePath, readOnly: false);
        await OpenConnectionAsync(context, cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }

    internal static async Task SaveAsync(string databasePath, TranslationProject project, CancellationToken cancellationToken)
    {
        await using ProjectDbContext context = CreateContext(databasePath, readOnly: false);
        await OpenConnectionAsync(context, cancellationToken);
        context.Projects.Add(new ProjectMetadata
        {
            Id = project.Id,
            Name = project.Name,
            SeriesName = project.SeriesName,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt,
            FormatVersion = ProjectContextFiles.FormatVersion
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    internal static async Task<TranslationProject> ReadAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using ProjectDbContext context = CreateContext(databasePath, readOnly: true);
        await OpenConnectionAsync(context, cancellationToken);
        await using DbCommand identityCommand = context.Database.GetDbConnection().CreateCommand();
        identityCommand.CommandText = "PRAGMA application_id;";
        object? identity = await identityCommand.ExecuteScalarAsync(cancellationToken);
        if (identity is not long applicationId || applicationId != ApplicationId)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "The database is not an INKLUME project database.");
        }

        string[] appliedMigrations = [.. (await context.Database.GetAppliedMigrationsAsync(cancellationToken))];
        string[] expectedMigrations = [.. context.Database.GetMigrations()];
        if (appliedMigrations.Length == 0)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "The database has no INKLUME migration history.");
        }

        if (!appliedMigrations.SequenceEqual(expectedMigrations, StringComparer.Ordinal))
        {
            throw new ProjectOperationException(ProjectErrorCode.IncompatibleVersion,
                "The database schema is not supported by this version of INKLUME.");
        }

        List<ProjectMetadata> records = await context.Projects.AsNoTracking().Take(2).ToListAsync(cancellationToken);
        if (records.Count != 1)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "The database must contain exactly one completed project.");
        }

        ProjectMetadata metadata = records[0];
        if (metadata.FormatVersion != ProjectContextFiles.FormatVersion)
        {
            throw new ProjectOperationException(ProjectErrorCode.IncompatibleVersion,
                "The project format is not supported by this version of INKLUME.");
        }

        try
        {
            return new TranslationProject(metadata.Id, metadata.Name, metadata.SeriesName,
                metadata.CreatedAt, metadata.UpdatedAt);
        }
        catch (ArgumentException exception)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "The stored project metadata is invalid.", exception);
        }
    }

    private static async Task OpenConnectionAsync(ProjectDbContext context, CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA temp_store = MEMORY;", cancellationToken);
    }

    private static ProjectDbContext CreateContext(string databasePath, bool readOnly)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false
        };
        DbContextOptions<ProjectDbContext> options = new DbContextOptionsBuilder<ProjectDbContext>()
            .UseSqlite(connectionString.ToString())
            .Options;
        return new ProjectDbContext(options);
    }
}
