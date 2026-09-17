using System.Globalization;
using Inklume.Domain.TextRegions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inklume.Infrastructure.Persistence;

public sealed class ProjectDbContext(DbContextOptions<ProjectDbContext> options) : DbContext(options)
{
    internal DbSet<ProjectMetadata> Projects => Set<ProjectMetadata>();

    internal DbSet<ChapterMetadata> Chapters => Set<ChapterMetadata>();

    internal DbSet<PageMetadata> Pages => Set<PageMetadata>();

    internal DbSet<TextRegionMetadata> TextRegions => Set<TextRegionMetadata>();

    internal DbSet<TextRegionPointMetadata> TextRegionPoints => Set<TextRegionPointMetadata>();

    internal DbSet<OcrRecognitionMetadata> OcrRecognitions => Set<OcrRecognitionMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<ProjectMetadata> project = modelBuilder.Entity<ProjectMetadata>();
        project.ToTable("Projects");
        project.HasKey(metadata => metadata.Id);
        project.Property(metadata => metadata.Id).ValueGeneratedNever();
        project.Property(metadata => metadata.Name).HasMaxLength(200).IsRequired();
        project.Property(metadata => metadata.SeriesName).HasMaxLength(200).IsRequired();
        project.Property(metadata => metadata.CreatedAt).IsRequired();
        project.Property(metadata => metadata.UpdatedAt).IsRequired();
        project.Property(metadata => metadata.FormatVersion).IsRequired();

        EntityTypeBuilder<ChapterMetadata> chapter = modelBuilder.Entity<ChapterMetadata>();
        chapter.ToTable("Chapters");
        chapter.HasKey(metadata => metadata.Id);
        chapter.Property(metadata => metadata.Id).ValueGeneratedNever();
        chapter.Property(metadata => metadata.ProjectId).IsRequired();
        chapter.Property(metadata => metadata.Number)
            .HasConversion(
                value => value.ToString("0.############################", CultureInfo.InvariantCulture),
                value => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture))
            .HasMaxLength(30)
            .IsRequired();
        chapter.Property(metadata => metadata.Title).HasMaxLength(200);
        chapter.Property(metadata => metadata.CreatedAt).IsRequired();
        chapter.Property(metadata => metadata.UpdatedAt).IsRequired();
        chapter.HasIndex(metadata => new { metadata.ProjectId, metadata.Number }).IsUnique();
        chapter.HasOne<ProjectMetadata>().WithMany().HasForeignKey(metadata => metadata.ProjectId).OnDelete(DeleteBehavior.Cascade);

        EntityTypeBuilder<PageMetadata> page = modelBuilder.Entity<PageMetadata>();
        page.HasKey(metadata => metadata.Id);
        page.Property(metadata => metadata.Id).ValueGeneratedNever();
        page.Property(metadata => metadata.ChapterId).IsRequired();
        page.Property(metadata => metadata.Number).IsRequired();
        page.Property(metadata => metadata.OriginalFileName).HasMaxLength(255).IsRequired();
        page.Property(metadata => metadata.RelativePath).HasMaxLength(1024).IsRequired();
        page.Property(metadata => metadata.ContentHash).HasMaxLength(64).IsFixedLength().IsRequired();
        page.Property(metadata => metadata.PixelWidth);
        page.Property(metadata => metadata.PixelHeight);
        page.HasIndex(metadata => new { metadata.ChapterId, metadata.Number }).IsUnique();
        page.HasIndex(metadata => new { metadata.ChapterId, metadata.RelativePath }).IsUnique();
        page.HasOne<ChapterMetadata>().WithMany().HasForeignKey(metadata => metadata.ChapterId).OnDelete(DeleteBehavior.Cascade);

        page.ToTable("Pages", table =>
        {
            table.HasCheckConstraint("CK_Pages_Number_Positive", "\"Number\" > 0");
            table.HasCheckConstraint("CK_Pages_ContentHash_Length", "length(\"ContentHash\") = 64");
            table.HasCheckConstraint(
                "CK_Pages_Dimensions_Valid",
                "(\"PixelWidth\" IS NULL AND \"PixelHeight\" IS NULL) OR (\"PixelWidth\" > 0 AND \"PixelHeight\" > 0)");
        });

        EntityTypeBuilder<TextRegionMetadata> region = modelBuilder.Entity<TextRegionMetadata>();
        region.ToTable("TextRegions", table =>
        {
            table.HasCheckConstraint("CK_TextRegions_ReadingOrder_Positive", "\"ReadingOrder\" > 0");
            table.HasCheckConstraint("CK_TextRegions_ReviewStatus_Valid", "\"ReviewStatus\" IN (0, 1)");
        });
        region.HasKey(metadata => metadata.Id);
        region.Property(metadata => metadata.Id).ValueGeneratedNever();
        region.Property(metadata => metadata.PageId).IsRequired();
        region.Property(metadata => metadata.GeometryKind).IsRequired();
        region.Property(metadata => metadata.ReadingOrder).IsRequired();
        region.Property(metadata => metadata.Role).IsRequired();
        region.Property(metadata => metadata.ContainerType).IsRequired();
        region.Property(metadata => metadata.Origin).HasDefaultValue(TextRegionOrigin.Manual).IsRequired();
        region.Property(metadata => metadata.ReviewedText);
        region.Property(metadata => metadata.ReviewStatus).HasDefaultValue(TextRegionReviewStatus.Pending).IsRequired();
        region.Property(metadata => metadata.ReviewedAt);
        region.Property(metadata => metadata.UserModifiedAt);
        region.Property(metadata => metadata.CreatedAt).IsRequired();
        region.Property(metadata => metadata.UpdatedAt).IsRequired();
        region.HasIndex(metadata => new { metadata.PageId, metadata.ReadingOrder }).IsUnique();
        region.HasOne<PageMetadata>().WithMany().HasForeignKey(metadata => metadata.PageId).OnDelete(DeleteBehavior.Cascade);
        region.HasMany(metadata => metadata.Points).WithOne()
            .HasForeignKey(metadata => metadata.TextRegionId).OnDelete(DeleteBehavior.Cascade);

        EntityTypeBuilder<TextRegionPointMetadata> point = modelBuilder.Entity<TextRegionPointMetadata>();
        point.ToTable("TextRegionPoints", table =>
        {
            table.HasCheckConstraint("CK_TextRegionPoints_PointIndex_Nonnegative", "\"PointIndex\" >= 0");
        });
        point.HasKey(metadata => new { metadata.TextRegionId, metadata.PointIndex });
        point.Property(metadata => metadata.X).IsRequired();
        point.Property(metadata => metadata.Y).IsRequired();

        EntityTypeBuilder<OcrRecognitionMetadata> ocr = modelBuilder.Entity<OcrRecognitionMetadata>();
        ocr.ToTable("OcrRecognitions", table =>
        {
            table.HasCheckConstraint(
                "CK_OcrRecognitions_RecognitionConfidence_Range",
                "\"RecognitionConfidence\" >= 0.0 AND \"RecognitionConfidence\" <= 1.0");
            table.HasCheckConstraint(
                "CK_OcrRecognitions_DetectionConfidence_Range",
                "\"DetectionConfidence\" IS NULL OR (\"DetectionConfidence\" >= 0.0 AND \"DetectionConfidence\" <= 1.0)");
        });
        ocr.HasKey(metadata => metadata.Id);
        ocr.Property(metadata => metadata.Id).ValueGeneratedNever();
        ocr.Property(metadata => metadata.TextRegionId).IsRequired();
        ocr.Property(metadata => metadata.Text).IsRequired();
        ocr.Property(metadata => metadata.RecognitionConfidence).IsRequired();
        ocr.Property(metadata => metadata.DetectionConfidence);
        ocr.Property(metadata => metadata.EngineName).HasMaxLength(100).IsRequired();
        ocr.Property(metadata => metadata.EngineVersion).HasMaxLength(50).IsRequired();
        ocr.Property(metadata => metadata.DetectionModel).HasMaxLength(100).IsRequired();
        ocr.Property(metadata => metadata.RecognitionModel).HasMaxLength(100).IsRequired();
        ocr.Property(metadata => metadata.ModelProfile).HasMaxLength(100).IsRequired();
        ocr.Property(metadata => metadata.CreatedAt).IsRequired();
        ocr.Property(metadata => metadata.UpdatedAt).IsRequired();
        ocr.HasIndex(metadata => metadata.TextRegionId).IsUnique();
        ocr.HasOne<TextRegionMetadata>().WithOne()
            .HasForeignKey<OcrRecognitionMetadata>(metadata => metadata.TextRegionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
