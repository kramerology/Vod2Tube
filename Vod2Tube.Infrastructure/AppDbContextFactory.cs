using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vod2Tube.Infrastructure
{
    /// <summary>
    /// Design-time factory used by <c>dotnet ef migrations</c> to create a
    /// <see cref="AppDbContext"/> without a running host.
    /// </summary>
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlite("Data Source=vod2tube-design.db");
            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
