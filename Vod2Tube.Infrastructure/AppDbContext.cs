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


        }
    }
}
