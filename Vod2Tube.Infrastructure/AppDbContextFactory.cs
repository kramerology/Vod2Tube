using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vod2Tube.Infrastructure
{
    /// <summary>
    /// Design-time factory used by EF Core tools (e.g. <c>dotnet ef migrations add</c>).
    /// </summary>
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite("Data Source=Vod2Tube.db")
                .Options;
            return new AppDbContext(options);
        }
    }
}
