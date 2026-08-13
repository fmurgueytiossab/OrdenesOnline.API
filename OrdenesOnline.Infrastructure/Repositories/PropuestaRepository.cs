using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;

namespace OrdenesOnline.Infrastructure.Repositories;

public sealed class PropuestaRepository : IPropuestaRepository
{
    private readonly AppDbContext _context;

    public PropuestaRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Propuesta>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _context.Propuestas.AsNoTracking().ToListAsync(cancellationToken);

    public async Task AddAsync(Propuesta propuesta, CancellationToken cancellationToken = default)
    {
        await _context.Propuestas.AddAsync(propuesta, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Propuesta propuesta, CancellationToken cancellationToken = default)
    {
        _context.Propuestas.Update(propuesta);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Propuestas.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            return;
        }

        _context.Propuestas.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<Propuesta?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _context.Propuestas.AsNoTracking().FirstOrDefaultAsync(x => x.PropuestaId == id, cancellationToken);
}
