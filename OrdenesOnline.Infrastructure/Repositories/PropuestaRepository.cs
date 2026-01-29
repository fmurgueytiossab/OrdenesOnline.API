using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;

namespace OrdenesOnline.Infrastructure.Repositories
{
    public class PropuestaRepository : IPropuestaRepository
    {
        private readonly AppDbContext _context;

        public PropuestaRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Propuesta>> GetAllAsync()
        {
            return await _context.Propuestas.ToListAsync();
        }

        public async Task AddAsync(Propuesta cliente)
        {
            await _context.Propuestas.AddAsync(cliente);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Propuesta cliente)
        {
            _context.Propuestas.Update(cliente);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _context.Propuestas.FindAsync(id);
            if (entity != null)
            {
                _context.Propuestas.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<Propuesta?> GetByIdAsync(int id)
        {
            return await _context.Propuestas.FindAsync(id);
        }
    }
}
