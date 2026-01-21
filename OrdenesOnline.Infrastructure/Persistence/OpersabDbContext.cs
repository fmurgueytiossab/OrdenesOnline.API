using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.entities;

namespace OrdenesOnline.Infrastructure.Persistence
{
    public class OpersabDbContext : DbContext
    {
        public OpersabDbContext(DbContextOptions<OpersabDbContext> options) : base(options)
        {
        }

        public DbSet<Valor> Valores { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Valor>()
                .ToTable("Valores");           
        }
    }
}
