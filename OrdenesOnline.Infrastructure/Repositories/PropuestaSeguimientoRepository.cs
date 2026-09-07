using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;

namespace OrdenesOnline.Infrastructure.Repositories;

public sealed class PropuestaSeguimientoRepository : IPropuestaSeguimientoRepository
{
    private readonly AppDbContext _appContext;
    private readonly IRepresentanteClientScopeRepository _clientScopeRepository;

    public PropuestaSeguimientoRepository(
        AppDbContext appContext,
        IRepresentanteClientScopeRepository clientScopeRepository)
    {
        _appContext = appContext;
        _clientScopeRepository = clientScopeRepository;
    }

    public async Task<PropuestaSeguimientoSnapshot> GetAsync(
        int representanteId,
        CancellationToken cancellationToken = default)
    {
        var scope = await _clientScopeRepository.GetAsync(representanteId, cancellationToken);
        if (!scope.RepresentanteExiste || scope.Cosabcli.Count == 0)
            return new(scope.RepresentanteExiste, []);

        var (start, end) = PropuestaTrackingDay.GetUtcRange(DateTimeOffset.UtcNow);
        var proposals = new Dictionary<int, Propuesta>();
        foreach (var clients in scope.Cosabcli.Chunk(500))
        {
            var batch = await _appContext.Propuestas.AsNoTracking()
                .Where(p => p.FechaRegistro >= start && p.FechaRegistro < end
                    && clients.Contains(p.Cosabcli))
                .ToListAsync(cancellationToken);
            foreach (var proposal in batch) proposals[proposal.PropuestaId] = proposal;
        }

        return new(true, proposals.Values.ToList());
    }
}
