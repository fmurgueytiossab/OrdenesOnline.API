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
        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<ClienteApoderado> ClientesApoderados { get; set; }
        public DbSet<ClienteBloqueo> ClientesBloqueos { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Valor>()
                .ToTable("Valores");

            modelBuilder.Entity<Cliente>()
                .ToTable("clientes");

            modelBuilder.Entity<ClienteApoderado>(entity =>
            {
                entity.HasNoKey();
                entity.ToTable("clientes_apoderados");
            });

            modelBuilder.Entity<ClienteBloqueo>(entity =>
            {
                entity.HasNoKey();
                entity.ToTable("clientes_bloqueos");
            });
        }
    }
}
