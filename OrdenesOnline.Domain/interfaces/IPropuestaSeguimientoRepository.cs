using OrdenesOnline.Domain.entities;

namespace OrdenesOnline.Domain.interfaces;

public interface IPropuestaSeguimientoRepository
{
    Task<PropuestaSeguimientoSnapshot> GetAsync(
        int representanteId,
        CancellationToken cancellationToken = default);
}
