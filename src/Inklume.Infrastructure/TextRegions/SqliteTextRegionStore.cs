using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;
using Inklume.Infrastructure.Persistence;
using Inklume.Infrastructure.Projects;
using Microsoft.EntityFrameworkCore;

namespace Inklume.Infrastructure.TextRegions;

public sealed class SqliteTextRegionStore : ITextRegionStore
{
    private const double GeometryTolerance = 0.000001;

    public async Task<Page?> GetPageAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        ProjectWorkspace verified = await VerifyWorkspaceAsync(workspace, cancellationToken);
        await using ProjectDbContext context = CreateContext(verified, readOnly: true);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);
        PageMetadata? metadata = await context.Pages.AsNoTracking()
            .Join(
                context.Chapters.Where(chapter => chapter.ProjectId == verified.Project.Id),
                page => page.ChapterId,
                chapter => chapter.Id,
                (page, chapter) => page)
            .SingleOrDefaultAsync(page => page.Id == pageId, cancellationToken);
        return metadata is null ? null : ToDomainPage(metadata);
    }

    public async Task<IReadOnlyList<TextRegion>> GetForPageAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        ProjectWorkspace verified = await VerifyWorkspaceAsync(workspace, cancellationToken);
        await using ProjectDbContext context = CreateContext(verified, readOnly: true);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);
        await RequirePageAsync(context, verified.Project.Id, pageId, cancellationToken);
        List<TextRegionMetadata> records = await context.TextRegions.AsNoTracking()
            .Include(region => region.Points)
            .Where(region => region.PageId == pageId)
            .OrderBy(region => region.ReadingOrder)
            .ToListAsync(cancellationToken);

        try
        {
            return records.Select(ToDomainRegion).ToArray();
        }
        catch (ArgumentException exception)
        {
            throw new ProjectOperationException(
                ProjectErrorCode.InvalidProject, "Stored text region metadata is invalid.", exception);
        }
    }

    public async Task<int> GetNextReadingOrderAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        ProjectWorkspace verified = await VerifyWorkspaceAsync(workspace, cancellationToken);
        await using ProjectDbContext context = CreateContext(verified, readOnly: true);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);
        await RequirePageAsync(context, verified.Project.Id, pageId, cancellationToken);
        int? maximum = await context.TextRegions.AsNoTracking()
            .Where(region => region.PageId == pageId)
            .MaxAsync(region => (int?)region.ReadingOrder, cancellationToken);
        return checked((maximum ?? 0) + 1);
    }

    public async Task AddAsync(
        ProjectWorkspace workspace,
        TextRegion region,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(region);
        ProjectWorkspace verified = await VerifyWorkspaceAsync(workspace, cancellationToken);
        await using ProjectDbContext context = CreateContext(verified, readOnly: false);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);
        await RequirePageAsync(context, verified.Project.Id, region.PageId, cancellationToken);
        context.TextRegions.Add(ToMetadata(region));
        await SaveChangesAsync(context, "The text region could not be created.", cancellationToken);
    }

    public async Task UpdateAsync(
        ProjectWorkspace workspace,
        TextRegion region,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(region);
        ProjectWorkspace verified = await VerifyWorkspaceAsync(workspace, cancellationToken);
        await using ProjectDbContext context = CreateContext(verified, readOnly: false);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);
        TextRegionMetadata metadata = await RequireRegionAsync(
            context, verified.Project.Id, region.Id, cancellationToken);
        if (metadata.PageId != region.PageId || metadata.ReadingOrder != region.ReadingOrder)
        {
            throw new ProjectOperationException(
                ProjectErrorCode.InvalidRegion, "The text region identity or reading order cannot be changed here.");
        }

        metadata.Role = region.Role;
        metadata.ContainerType = region.ContainerType;
        metadata.ReviewedText = region.ReviewedText;
        metadata.ReviewStatus = region.ReviewStatus;
        metadata.ReviewedAt = region.ReviewedAt;
        metadata.UserModifiedAt = region.UserModifiedAt;
        metadata.UpdatedAt = region.UpdatedAt;
        await SaveChangesAsync(context, "The text region could not be updated.", cancellationToken);
    }

    public async Task ReorderPageRegionsAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        IReadOnlyList<Guid> orderedRegionIds,
        DateTimeOffset userModifiedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedRegionIds);
        ProjectWorkspace verified = await VerifyWorkspaceAsync(workspace, cancellationToken);
        await using ProjectDbContext context = CreateContext(verified, readOnly: false);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);
        await RequirePageAsync(context, verified.Project.Id, pageId, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        List<TextRegionMetadata> regions = await context.TextRegions
            .Where(r => r.PageId == pageId)
            .ToListAsync(cancellationToken);

        var regionMap = regions.ToDictionary(r => r.Id);

        // Step 1: Assign temporary positive offset reading orders to avoid unique index violation on (PageId, ReadingOrder)
        // while satisfying CK_TextRegions_ReadingOrder_Positive ("ReadingOrder" > 0)
        int tempOrder = 1_000_000;
        foreach (Guid regionId in orderedRegionIds)
        {
            if (regionMap.TryGetValue(regionId, out TextRegionMetadata? meta))
            {
                meta.ReadingOrder = tempOrder++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        // Step 2: Assign final sequence 1..N and update UserModifiedAt
        int order = 1;
        foreach (Guid regionId in orderedRegionIds)
        {
            if (regionMap.TryGetValue(regionId, out TextRegionMetadata? meta))
            {
                meta.ReadingOrder = order++;
                meta.UserModifiedAt = userModifiedAt;
                meta.UpdatedAt = userModifiedAt;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        ProjectWorkspace workspace,
        Guid regionId,
        CancellationToken cancellationToken)
    {
        ProjectWorkspace verified = await VerifyWorkspaceAsync(workspace, cancellationToken);
        await using ProjectDbContext context = CreateContext(verified, readOnly: false);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);
        TextRegionMetadata metadata = await RequireRegionAsync(
            context, verified.Project.Id, regionId, cancellationToken);
        context.TextRegions.Remove(metadata);
        await SaveChangesAsync(context, "The text region could not be deleted.", cancellationToken);
    }

    private static ProjectDbContext CreateContext(ProjectWorkspace workspace, bool readOnly)
        => SqliteProjectPersistence.CreateContext(workspace.DatabasePath, readOnly);

    private static async Task<ProjectWorkspace> VerifyWorkspaceAsync(
        ProjectWorkspace workspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!File.Exists(workspace.DatabasePath))
        {
            throw new ProjectOperationException(
                ProjectErrorCode.NotFound, "The workspace database could not be found.");
        }

        TranslationProject project = await SqliteProjectPersistence.ReadAsync(workspace.DatabasePath, cancellationToken);
        if (project.Id != workspace.Project.Id)
        {
            throw new ProjectOperationException(
                ProjectErrorCode.InvalidProject, "The selected workspace no longer matches the open project.");
        }

        return workspace with { Project = project };
    }

    private static async Task RequirePageAsync(
        ProjectDbContext context,
        Guid projectId,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        bool exists = await context.Pages.AsNoTracking()
            .Join(
                context.Chapters.Where(chapter => chapter.ProjectId == projectId),
                page => page.ChapterId,
                chapter => chapter.Id,
                (page, chapter) => page)
            .AnyAsync(page => page.Id == pageId, cancellationToken);
        if (!exists)
        {
            throw new ProjectOperationException(
                ProjectErrorCode.InvalidRegion, "The selected page does not belong to the open project.");
        }
    }

    private static async Task<TextRegionMetadata> RequireRegionAsync(
        ProjectDbContext context,
        Guid projectId,
        Guid regionId,
        CancellationToken cancellationToken)
    {
        TextRegionMetadata? metadata = await context.TextRegions
            .Join(context.Pages, region => region.PageId, page => page.Id, (region, page) => new { region, page })
            .Join(
                context.Chapters.Where(chapter => chapter.ProjectId == projectId),
                value => value.page.ChapterId,
                chapter => chapter.Id,
                (value, chapter) => value.region)
            .SingleOrDefaultAsync(region => region.Id == regionId, cancellationToken);
        return metadata ?? throw new ProjectOperationException(
            ProjectErrorCode.InvalidRegion, "The selected text region does not belong to the open project.");
    }

    private static async Task SaveChangesAsync(
        ProjectDbContext context,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidRegion, message, exception);
        }
    }

    private static Page ToDomainPage(PageMetadata metadata)
        => new(metadata.Id, metadata.ChapterId, metadata.Number, metadata.OriginalFileName,
            metadata.RelativePath, metadata.ContentHash, metadata.PixelWidth, metadata.PixelHeight);

    private static TextRegionMetadata ToMetadata(TextRegion region)
        => new()
        {
            Id = region.Id,
            PageId = region.PageId,
            GeometryKind = region.Geometry.Kind,
            ReadingOrder = region.ReadingOrder,
            Role = region.Role,
            ContainerType = region.ContainerType,
            Origin = region.Origin,
            ReviewedText = region.ReviewedText,
            ReviewStatus = region.ReviewStatus,
            ReviewedAt = region.ReviewedAt,
            UserModifiedAt = region.UserModifiedAt,
            CreatedAt = region.CreatedAt,
            UpdatedAt = region.UpdatedAt,
            Points = region.Geometry.Points.Select((point, index) => new TextRegionPointMetadata
            {
                TextRegionId = region.Id,
                PointIndex = index,
                X = point.X,
                Y = point.Y
            }).ToList()
        };

    private static TextRegion ToDomainRegion(TextRegionMetadata metadata)
    {
        ImagePoint[] points = [.. metadata.Points
            .OrderBy(point => point.PointIndex)
            .Select(point => new ImagePoint(point.X, point.Y))];
        TextRegionGeometry geometry = metadata.GeometryKind switch
        {
            TextRegionGeometryKind.Rectangle => RestoreRectangle(points),
            TextRegionGeometryKind.Polygon => new PolygonTextRegionGeometry(points),
            _ => throw new ArgumentException("The stored geometry kind is not supported.")
        };
        return new TextRegion(
            metadata.Id, metadata.PageId, geometry, metadata.ReadingOrder, metadata.Role,
            metadata.ContainerType, metadata.CreatedAt, metadata.UpdatedAt, metadata.Origin,
            metadata.ReviewedText, metadata.ReviewStatus, metadata.ReviewedAt, metadata.UserModifiedAt);
    }

    private static RectangleTextRegionGeometry RestoreRectangle(IReadOnlyList<ImagePoint> points)
    {
        if (points.Count != 4)
        {
            throw new ArgumentException("A stored rectangle must contain exactly four points.");
        }

        ImagePoint topLeft = points[0];
        ImagePoint topRight = points[1];
        ImagePoint bottomRight = points[2];
        ImagePoint bottomLeft = points[3];
        bool orderedRectangle = NearlyEqual(topLeft.Y, topRight.Y)
            && NearlyEqual(topRight.X, bottomRight.X)
            && NearlyEqual(bottomRight.Y, bottomLeft.Y)
            && NearlyEqual(bottomLeft.X, topLeft.X);
        if (!orderedRectangle)
        {
            throw new ArgumentException("Stored rectangle points are not in canonical order.");
        }

        return new RectangleTextRegionGeometry(
            topLeft.X, topLeft.Y, topRight.X - topLeft.X, bottomLeft.Y - topLeft.Y);
    }

    private static bool NearlyEqual(double first, double second)
        => Math.Abs(first - second) <= GeometryTolerance;
}
