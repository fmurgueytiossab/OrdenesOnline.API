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
        public DbSet<CodeRepresentante> CodeRepresentantes { get; set; }
        public DbSet<ActionToken> ActionTokens { get; set; }
        public DbSet<PasswordValidationResult> PasswordValidationResults { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Propuesta>()
                .ToTable("propuesta")
                .Property(propuesta => propuesta.Estado)
                .HasMaxLength(20);

            modelBuilder.Entity<ActionToken>(entity =>
            {
                entity.ToTable("Token", table => table.HasCheckConstraint(
                    "CK_Token_Type", "[Type] IN ('proposal_review','password_reset')"));
                entity.HasIndex(token => token.TokenHash).IsUnique();
                entity.HasIndex(token => new { token.UserId, token.Type });
                entity.Property(token => token.TokenHash).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
                entity.Property(token => token.Type).HasMaxLength(20).IsUnicode(false).IsRequired();
                entity.Property(token => token.CreatedAt).HasPrecision(3);
                entity.Property(token => token.ExpiresAt).HasPrecision(3);
            });

            modelBuilder.Entity<Representante>()
                .ToTable("UserRepresentante");

            modelBuilder.Entity<CodeRepresentante>()
                .ToTable("CodeRepresentante")
                .HasKey(x => new { x.RepresentanteId, x.Cosabcli });

            modelBuilder.Entity<PasswordValidationResult>().HasNoKey();
        }
    }
}
