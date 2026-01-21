using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

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

        public async Task<Representante> GetByIdAsync(int id)
        {
            return await _context.Representantes.FindAsync(id);
        }

        public async Task<PasswordValidationResult?> ValidatePassword(string correo,string password)
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


    }
}
