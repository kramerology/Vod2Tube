using Microsoft.EntityFrameworkCore;
using Vod2Tube.Domain;


namespace Vod2Tube.Infrastructure
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        // DbSet for Channel entity
        public DbSet<Channel> Channels { get; set; }
        public DbSet<TwitchVod> TwitchVods { get; set; }
        public DbSet<Pipeline> Pipelines { get; set; }
        public DbSet<Setting> Settings { get; set; }
        public DbSet<YouTubeAccount> YouTubeAccounts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Channel entity
            modelBuilder.Entity<Channel>(entity =>
            {
                entity.HasKey(c => c.Id);
            });

            modelBuilder.Entity<TwitchVod>(entity =>
            {
                entity.HasKey(tv => tv.Id);
            });
            modelBuilder.Entity<Pipeline>(entity =>
            {
                entity.HasKey(p => p.VodId);
            });

            modelBuilder.Entity<Setting>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.HasIndex(s => s.Key).IsUnique();
            });

            modelBuilder.Entity<YouTubeAccount>(entity =>
            {
                entity.HasKey(a => a.Id);
            });
        }
    }
}
