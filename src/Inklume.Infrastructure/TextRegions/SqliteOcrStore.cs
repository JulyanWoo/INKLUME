using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;
using Inklume.Infrastructure.Persistence;
using Inklume.Infrastructure.Projects;
using Microsoft.EntityFrameworkCore;

namespace Inklume.Infrastructure.TextRegions;

public sealed class SqliteOcrStore(TimeProvider? timeProvider = null) : IOcrStore
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyDictionary<Guid, OcrRecognition>> GetRecognitionsForPageAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        ProjectWorkspace verified = await VerifyWorkspaceAsync(workspace, cancellationToken);
        await using ProjectDbContext context = CreateContext(verified, readOnly: true);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);

        List<OcrRecognitionMetadata> records = await context.OcrRecognitions.AsNoTracking()
            .Join(
                context.TextRegions.Where(region => region.PageId == pageId),
                ocr => ocr.TextRegionId,
                region => region.Id,
                (ocr, region) => ocr)
            .ToListAsync(cancellationToken);

        return records.ToDictionary(r => r.TextRegionId, ToDomainRecognition);
    }

    public async Task<OcrRecognition?> GetRecognitionForRegionAsync(
        ProjectWorkspace workspace,
        Guid regionId,
        CancellationToken cancellationToken)
    {
        ProjectWorkspace verified = await VerifyWorkspaceAsync(workspace, cancellationToken);
        await using ProjectDbContext context = CreateContext(verified, readOnly: true);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);

        OcrRecognitionMetadata? record = await context.OcrRecognitions.AsNoTracking()
            .SingleOrDefaultAsync(ocr => ocr.TextRegionId == regionId, cancellationToken);

        return record is null ? null : ToDomainRecognition(record);
    }

    public async Task<IReadOnlyList<TextRegion>> ReplacePageOcrRegionsAsync(
        ProjectWorkspace workspace,
        Guid pageId,
        IReadOnlyList<OcrDetectedRegion> detectedRegions,
        OcrEngineMetadata engineMetadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detectedRegions);
        ArgumentNullException.ThrowIfNull(engineMetadata);

        ProjectWorkspace verified = await VerifyWorkspaceAsync(workspace, cancellationToken);
        await using ProjectDbContext context = CreateContext(verified, readOnly: false);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Verify page exists
        bool pageExists = await context.Pages.AsNoTracking()
            .Join(
                context.Chapters.Where(c => c.ProjectId == verified.Project.Id),
                p => p.ChapterId,
                c => c.Id,
                (p, c) => p)
            .AnyAsync(p => p.Id == pageId, cancellationToken);

        if (!pageExists)
        {
            throw new ProjectOperationException(
                ProjectErrorCode.InvalidRegion,
                "The selected page does not belong to the open project.");
        }

        // Get existing regions on this page with points
        List<TextRegionMetadata> existingRegions = await context.TextRegions
            .Include(r => r.Points)
            .Where(r => r.PageId == pageId)
            .ToListAsync(cancellationToken);

        List<TextRegionMetadata> protectedRegions = existingRegions
            .Where(IsProtected)
            .ToList();

        List<TextRegionMetadata> autoRegionsToDelete = existingRegions
            .Where(r => !IsProtected(r))
            .ToList();

        context.TextRegions.RemoveRange(autoRegionsToDelete);

        // Determine starting reading order after preserved protected regions
        int maxPreservedOrder = protectedRegions
            .Select(r => r.ReadingOrder)
            .DefaultIfEmpty(0)
            .Max();

        // Extract protected bounding boxes for overlap suppression
        var protectedBounds = protectedRegions.Select(r =>
        {
            if (r.Points.Count == 0)
            {
                return new ImageBounds(0, 0, 0, 0);
            }

            double minX = r.Points.Min(p => p.X);
            double minY = r.Points.Min(p => p.Y);
            double maxX = r.Points.Max(p => p.X);
            double maxY = r.Points.Max(p => p.Y);
            return new ImageBounds(minX, minY, maxX - minX, maxY - minY);
        }).ToList();

        // Sort detected regions deterministically: top-to-bottom (Y center), then left-to-right (X center)
        List<OcrDetectedRegion> sortedDetections = detectedRegions
            .OrderBy(d => d.Geometry.Bounds.Y + (d.Geometry.Bounds.Height / 2.0))
            .ThenBy(d => d.Geometry.Bounds.X + (d.Geometry.Bounds.Width / 2.0))
            .ToList();

        DateTimeOffset now = _timeProvider.GetUtcNow();
        var createdRegions = new List<TextRegion>();
        int readingOrder = maxPreservedOrder + 1;

        foreach (OcrDetectedRegion detected in sortedDetections)
        {
            // Part J4: Suppress new detection if it overlaps a protected region with IoU >= threshold (0.50)
            if (OcrOverlapPolicy.ShouldSuppressDetection(detected.Geometry.Bounds, protectedBounds))
            {
                continue;
            }

            Guid regionId = Guid.NewGuid();
            int currentReadingOrder = readingOrder++;
            var regionMetadata = new TextRegionMetadata
            {
                Id = regionId,
                PageId = pageId,
                GeometryKind = TextRegionGeometryKind.Polygon,
                ReadingOrder = currentReadingOrder,
                Role = TextRegionRole.Unknown,
                ContainerType = TextContainerType.Unknown,
                Origin = TextRegionOrigin.OcrDetection,
                ReviewedText = null,
                ReviewStatus = TextRegionReviewStatus.Pending,
                ReviewedAt = null,
                UserModifiedAt = null,
                CreatedAt = now,
                UpdatedAt = now,
                Points = detected.Geometry.Points.Select((pt, index) => new TextRegionPointMetadata
                {
                    TextRegionId = regionId,
                    PointIndex = index,
                    X = pt.X,
                    Y = pt.Y
                }).ToList()
            };

            var ocrMetadata = new OcrRecognitionMetadata
            {
                Id = Guid.NewGuid(),
                TextRegionId = regionId,
                Text = detected.Text,
                RecognitionConfidence = detected.RecognitionConfidence,
                DetectionConfidence = detected.DetectionConfidence,
                EngineName = engineMetadata.EngineName,
                EngineVersion = engineMetadata.EngineVersion,
                DetectionModel = engineMetadata.DetectionModel,
                RecognitionModel = engineMetadata.RecognitionModel,
                ModelProfile = engineMetadata.ModelProfile,
                CreatedAt = now,
                UpdatedAt = now
            };

            context.TextRegions.Add(regionMetadata);
            context.OcrRecognitions.Add(ocrMetadata);

            createdRegions.Add(new TextRegion(
                regionId,
                pageId,
                detected.Geometry,
                currentReadingOrder,
                TextRegionRole.Unknown,
                TextContainerType.Unknown,
                now,
                now,
                TextRegionOrigin.OcrDetection,
                null,
                TextRegionReviewStatus.Pending,
                null,
                null));
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        List<TextRegion> survivingRegions = [.. protectedRegions.Select(ToDomainRegion), .. createdRegions];
        return survivingRegions.OrderBy(r => r.ReadingOrder).ToList();
    }

    private static bool IsProtected(TextRegionMetadata region)
        => region.Origin == TextRegionOrigin.Manual
           || region.UserModifiedAt != null
           || region.ReviewStatus == TextRegionReviewStatus.Reviewed
           || region.ReviewedText != null
           || region.Role != TextRegionRole.Unknown
           || region.ContainerType != TextContainerType.Unknown;

    private static TextRegion ToDomainRegion(TextRegionMetadata metadata)
    {
        ImagePoint[] points = [.. metadata.Points
            .OrderBy(point => point.PointIndex)
            .Select(point => new ImagePoint(point.X, point.Y))];
        TextRegionGeometry geometry = metadata.GeometryKind switch
        {
            TextRegionGeometryKind.Rectangle => new RectangleTextRegionGeometry(
                points.Min(p => p.X),
                points.Min(p => p.Y),
                points.Max(p => p.X) - points.Min(p => p.X),
                points.Max(p => p.Y) - points.Min(p => p.Y)),
            TextRegionGeometryKind.Polygon => new PolygonTextRegionGeometry(points),
            _ => throw new ArgumentException("The stored geometry kind is not supported.")
        };
        return new TextRegion(
            metadata.Id, metadata.PageId, geometry, metadata.ReadingOrder, metadata.Role,
            metadata.ContainerType, metadata.CreatedAt, metadata.UpdatedAt, metadata.Origin,
            metadata.ReviewedText, metadata.ReviewStatus, metadata.ReviewedAt, metadata.UserModifiedAt);
    }

    public async Task<OcrRecognition> SaveRegionRecognitionAsync(
        ProjectWorkspace workspace,
        Guid regionId,
        OcrRegionResult regionResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(regionResult);

        ProjectWorkspace verified = await VerifyWorkspaceAsync(workspace, cancellationToken);
        await using ProjectDbContext context = CreateContext(verified, readOnly: false);
        await SqliteProjectPersistence.OpenConnectionAsync(context, cancellationToken);

        // Verify region exists
        TextRegionMetadata? region = await context.TextRegions
            .SingleOrDefaultAsync(r => r.Id == regionId, cancellationToken);

        if (region is null)
        {
            throw new ProjectOperationException(
                ProjectErrorCode.InvalidRegion,
                "The target text region does not exist.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        OcrRecognitionMetadata? existing = await context.OcrRecognitions
            .SingleOrDefaultAsync(ocr => ocr.TextRegionId == regionId, cancellationToken);

        if (existing is null)
        {
            existing = new OcrRecognitionMetadata
            {
                Id = Guid.NewGuid(),
                TextRegionId = regionId,
                Text = regionResult.Text,
                RecognitionConfidence = regionResult.RecognitionConfidence,
                DetectionConfidence = null,
                EngineName = regionResult.EngineInfo.EngineName,
                EngineVersion = regionResult.EngineInfo.EngineVersion,
                DetectionModel = regionResult.EngineInfo.DetectionModel,
                RecognitionModel = regionResult.EngineInfo.RecognitionModel,
                ModelProfile = regionResult.EngineInfo.ModelProfile,
                CreatedAt = now,
                UpdatedAt = now
            };
            context.OcrRecognitions.Add(existing);
        }
        else
        {
            existing.Text = regionResult.Text;
            existing.RecognitionConfidence = regionResult.RecognitionConfidence;
            existing.EngineName = regionResult.EngineInfo.EngineName;
            existing.EngineVersion = regionResult.EngineInfo.EngineVersion;
            existing.DetectionModel = regionResult.EngineInfo.DetectionModel;
            existing.RecognitionModel = regionResult.EngineInfo.RecognitionModel;
            existing.ModelProfile = regionResult.EngineInfo.ModelProfile;
            existing.UpdatedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);

        return ToDomainRecognition(existing);
    }

    private static OcrRecognition ToDomainRecognition(OcrRecognitionMetadata metadata)
        => new(
            metadata.Id,
            metadata.TextRegionId,
            metadata.Text,
            metadata.RecognitionConfidence,
            metadata.DetectionConfidence,
            metadata.EngineName,
            metadata.EngineVersion,
            metadata.DetectionModel,
            metadata.RecognitionModel,
            metadata.ModelProfile,
            metadata.CreatedAt,
            metadata.UpdatedAt);

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

    private static ProjectDbContext CreateContext(ProjectWorkspace workspace, bool readOnly)
        => SqliteProjectPersistence.CreateContext(workspace.DatabasePath, readOnly);
}
