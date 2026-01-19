using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.entities;

namespace OrdenesOnline.Infrastructure.Persistence
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Propuesta> Propuestas { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Propuesta>()
                .ToTable("propuestas");
        }
    }
}
