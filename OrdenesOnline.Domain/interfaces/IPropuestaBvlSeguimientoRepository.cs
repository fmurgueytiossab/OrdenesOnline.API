using OrdenesOnline.Domain.entities;

namespace OrdenesOnline.Domain.interfaces;

public interface IPropuestaBvlSeguimientoRepository
{
    Task<PropuestaBvlSeguimientoSnapshot> GetAsync(
        int representanteId,
        CancellationToken cancellationToken = default);
}
