using System.Data.Common;
using Inklume.Application.Projects;
using Inklume.Domain.Projects;
using Inklume.Infrastructure.Projects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inklume.Infrastructure.Persistence;

internal enum DatabaseProbeResult
{
    DoesNotExist,
    ValidInklumeProject,
    InvalidOrConflicting
}

internal static class SqliteProjectPersistence
{
    // SQLite's application_id distinguishes this format from unrelated SQLite databases.
    internal const int ApplicationId = 0x494E4B4C;

    internal static async Task<DatabaseProbeResult> ProbeDatabaseAsync(string databasePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            return DatabaseProbeResult.DoesNotExist;
        }

        try
        {
            await using ProjectDbContext probe = CreateContext(databasePath, readOnly: true);
            await OpenConnectionAsync(probe, cancellationToken);
            await VerifyIdentityAsync(probe, cancellationToken);

            string[] appliedMigrations = [.. (await probe.Database.GetAppliedMigrationsAsync(cancellationToken))];
            if (appliedMigrations.Length == 0)
            {
                return DatabaseProbeResult.InvalidOrConflicting;
            }

            int count = await probe.Projects.AsNoTracking().CountAsync(cancellationToken);
            if (count != 1)
            {
                return DatabaseProbeResult.InvalidOrConflicting;
            }

            return DatabaseProbeResult.ValidInklumeProject;
        }
        catch
        {
            return DatabaseProbeResult.InvalidOrConflicting;
        }
    }

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
        await EnsureCurrentSchemaAsync(databasePath, cancellationToken);
        await using ProjectDbContext context = CreateContext(databasePath, readOnly: true);
        await OpenConnectionAsync(context, cancellationToken);
        await VerifyIdentityAsync(context, cancellationToken);

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

    internal static async Task OpenConnectionAsync(ProjectDbContext context, CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA temp_store = MEMORY;", cancellationToken);
    }

    internal static ProjectDbContext CreateContext(string databasePath, bool readOnly)
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

    private static async Task EnsureCurrentSchemaAsync(string databasePath, CancellationToken cancellationToken)
    {
        string[] appliedMigrations;
        string[] expectedMigrations;
        await using (ProjectDbContext probe = CreateContext(databasePath, readOnly: true))
        {
            await OpenConnectionAsync(probe, cancellationToken);
            await VerifyIdentityAsync(probe, cancellationToken);
            appliedMigrations = [.. (await probe.Database.GetAppliedMigrationsAsync(cancellationToken))];
            expectedMigrations = [.. probe.Database.GetMigrations()];
        }

        if (appliedMigrations.Length == 0)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "The database has no INKLUME migration history.");
        }

        bool isKnownPrefix = appliedMigrations.Length <= expectedMigrations.Length
            && appliedMigrations.SequenceEqual(expectedMigrations.Take(appliedMigrations.Length), StringComparer.Ordinal);
        if (!isKnownPrefix)
        {
            throw new ProjectOperationException(ProjectErrorCode.IncompatibleVersion,
                "The database schema is not supported by this version of INKLUME.");
        }

        if (appliedMigrations.Length < expectedMigrations.Length)
        {
            await using ProjectDbContext upgrade = CreateContext(databasePath, readOnly: false);
            await OpenConnectionAsync(upgrade, cancellationToken);
            await upgrade.Database.MigrateAsync(cancellationToken);
        }

        await using ProjectDbContext verification = CreateContext(databasePath, readOnly: true);
        await OpenConnectionAsync(verification, cancellationToken);
        string[] finalMigrations = [.. (await verification.Database.GetAppliedMigrationsAsync(cancellationToken))];
        if (!finalMigrations.SequenceEqual(expectedMigrations, StringComparer.Ordinal))
        {
            throw new ProjectOperationException(ProjectErrorCode.IncompatibleVersion,
                "The database schema is not supported by this version of INKLUME.");
        }
    }

    private static async Task VerifyIdentityAsync(ProjectDbContext context, CancellationToken cancellationToken)
    {
        await using DbCommand identityCommand = context.Database.GetDbConnection().CreateCommand();
        identityCommand.CommandText = "PRAGMA application_id;";
        object? identity = await identityCommand.ExecuteScalarAsync(cancellationToken);
        if (identity is not long applicationId || applicationId != ApplicationId)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidProject,
                "The database is not an INKLUME project database.");
        }
    }
}
