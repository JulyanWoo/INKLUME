using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inklume.Infrastructure.Persistence;

public sealed class ProjectDbContext(DbContextOptions<ProjectDbContext> options) : DbContext(options)
{
    internal DbSet<ProjectMetadata> Projects => Set<ProjectMetadata>();

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
    }
}
