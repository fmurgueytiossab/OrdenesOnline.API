using OrdenesOnline.Domain.entities;

namespace OrdenesOnline.Domain.interfaces;

public interface IValorRepository
{
    Task<IEnumerable<Valor>> GetAllAsync(CancellationToken cancellationToken = default);
}
