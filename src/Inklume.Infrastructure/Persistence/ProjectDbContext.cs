using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inklume.Infrastructure.Persistence;

public sealed class ProjectDbContext(DbContextOptions<ProjectDbContext> options) : DbContext(options)
{
    internal DbSet<ProjectMetadata> Projects => Set<ProjectMetadata>();

    internal DbSet<ChapterMetadata> Chapters => Set<ChapterMetadata>();

    internal DbSet<PageMetadata> Pages => Set<PageMetadata>();

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
        page.ToTable("Pages", table =>
        {
            table.HasCheckConstraint("CK_Pages_Number_Positive", "\"Number\" > 0");
            table.HasCheckConstraint("CK_Pages_ContentHash_Length", "length(\"ContentHash\") = 64");
        });
        page.HasKey(metadata => metadata.Id);
        page.Property(metadata => metadata.Id).ValueGeneratedNever();
        page.Property(metadata => metadata.ChapterId).IsRequired();
        page.Property(metadata => metadata.Number).IsRequired();
        page.Property(metadata => metadata.OriginalFileName).HasMaxLength(255).IsRequired();
        page.Property(metadata => metadata.RelativePath).HasMaxLength(1024).IsRequired();
        page.Property(metadata => metadata.ContentHash).HasMaxLength(64).IsFixedLength().IsRequired();
        page.HasIndex(metadata => new { metadata.ChapterId, metadata.Number }).IsUnique();
        page.HasIndex(metadata => new { metadata.ChapterId, metadata.RelativePath }).IsUnique();
        page.HasOne<ChapterMetadata>().WithMany().HasForeignKey(metadata => metadata.ChapterId).OnDelete(DeleteBehavior.Cascade);
    }
}
