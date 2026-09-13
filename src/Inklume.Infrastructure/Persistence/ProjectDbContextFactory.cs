using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inklume.Infrastructure.Persistence;

public sealed class ProjectDbContextFactory : IDesignTimeDbContextFactory<ProjectDbContext>
{
    public ProjectDbContext CreateDbContext(string[] args)
    {
        // Design-time commands must not create or migrate a user's project database.
        DbContextOptions<ProjectDbContext> options = new DbContextOptionsBuilder<ProjectDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        return new ProjectDbContext(options);
    }
}
