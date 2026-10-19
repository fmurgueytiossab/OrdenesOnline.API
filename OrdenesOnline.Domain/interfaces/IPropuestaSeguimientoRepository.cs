using OrdenesOnline.Domain.entities;

namespace OrdenesOnline.Domain.interfaces;

public interface IPropuestaSeguimientoRepository
{
    Task<MarcarPropuestaIngresadaStatus> MarkEnteredAsync(
        int representanteId, int propuestaId, CancellationToken cancellationToken = default);

    Task<PropuestaSeguimientoSnapshot> GetAsync(
        int representanteId,
        CancellationToken cancellationToken = default);
}
