using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;

namespace OrdenesOnline.Infrastructure.Repositories
{
    public class RepresentanteRepository :IRepresentanteRepository
    {
        private readonly AppDbContext _context;

        public RepresentanteRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Representante>> GetAllAsync()
        {
            return await _context.Representantes.ToListAsync();
        }

        public async Task AddAsync(Representante cliente)
        {
            await _context.Representantes.AddAsync(cliente);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Representante representante)
        {
            _context.Representantes.Update(representante);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _context.Representantes.FindAsync(id);
            if (entity != null)
            {
                _context.Representantes.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<RepresentanteDTO> GetByIdAsync(int RepresentanteId)
        {
            var user = await _context.Representantes
                .Where(u => u.RepresentanteId == RepresentanteId)
                .Select(u => new RepresentanteDTO
                {
                    RepresentanteId = u.RepresentanteId,
                    Nombre = u.Nombre,
                    CorreoCorporativo = u.CorreoCorporativo,
                    Dni = u.Dni
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return null;

            user.Cosabcli = await _context.CodeRepresentantes
                .Where(c => c.RepresentanteId == RepresentanteId)
                .Select(c => c.Cosabcli)
                .ToListAsync();

            return user;
        }

        public async Task<PasswordValidationResult?> Login(string correo,string password)
        {
            var result = _context.Set<PasswordValidationResult>()
                .FromSqlRaw(
                    "EXEC usp_isValidRepresentantePassword @correo, @password",
                    new SqlParameter("@correo", correo),
                    new SqlParameter("@password", password)
                )
                .AsEnumerable()
                .FirstOrDefault();

            return result;
        }

        public async Task<bool> UpdatePassword(string correo, string password)
        {
            var rowsAffected = await _context.Database.ExecuteSqlRawAsync(
                "EXEC usp_updateRepresentantePassword @correo, @password",
                new SqlParameter("@correo", correo),
                new SqlParameter("@password", password)
            );

            return rowsAffected > 0;
        }

        public async Task<Representante?> GetByEmail(string email)
        {
            return await _context.Representantes.AsNoTracking()
                                 .FirstOrDefaultAsync(c => c.CorreoCorporativo == email);
        }
    }
}
