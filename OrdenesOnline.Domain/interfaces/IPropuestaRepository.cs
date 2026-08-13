using OrdenesOnline.Domain.entities;

namespace OrdenesOnline.Domain.interfaces;

public interface IPropuestaRepository
{
    Task<IEnumerable<Propuesta>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Propuesta?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task AddAsync(Propuesta propuesta, CancellationToken cancellationToken = default);
    Task UpdateAsync(Propuesta propuesta, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
