using OrdenesOnline.Domain.entities;

namespace OrdenesOnline.Domain.interfaces;

public interface IRepresentanteClientScopeRepository
{
    Task<RepresentanteClientScope> GetAsync(
        int representanteId,
        CancellationToken cancellationToken = default);
}
