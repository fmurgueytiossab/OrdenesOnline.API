using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;

namespace OrdenesOnline.Infrastructure.Persistence
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Propuesta> Propuestas { get; set; }
        public DbSet<Representante> Representantes { get; set; }
        public DbSet<PasswordValidationResult> PasswordValidationResults { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Propuesta>()
                .ToTable("propuestas");

            modelBuilder.Entity<Representante>()
                .ToTable("UserRepresentante");

            modelBuilder.Entity<PasswordValidationResult>().HasNoKey();
        }
    }
}
