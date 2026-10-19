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

    public async Task<MarcarPropuestaIngresadaStatus> MarkEnteredAsync(
        int representanteId, int propuestaId, CancellationToken cancellationToken = default)
    {
        var scope = await _clientScopeRepository.GetAsync(representanteId, cancellationToken);
        if (!scope.RepresentanteExiste) return MarcarPropuestaIngresadaStatus.RepresentanteNotFound;
        var proposal = await _appContext.Propuestas.AsNoTracking()
            .Where(p => p.PropuestaId == propuestaId)
            .Select(p => new { p.Cosabcli, p.Estado }).SingleOrDefaultAsync(cancellationToken);
        if (proposal is null || !scope.Cosabcli.Contains(proposal.Cosabcli.Trim(), StringComparer.OrdinalIgnoreCase))
            return MarcarPropuestaIngresadaStatus.NotFound;
        // Reintentar una solicitud ya aplicada no cambia la propuesta ni duplica acciones.
        if (proposal.Estado == PropuestaEstados.Ingresado) return MarcarPropuestaIngresadaStatus.Success;

        // La condicion y el cambio son una sola operacion: no basta con el estado recibido del navegador.
        var changed = await _appContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE p SET Estado={PropuestaEstados.Ingresado} FROM dbo.propuesta p
            WHERE p.PropuestaId={propuestaId} AND p.Cosabcli={proposal.Cosabcli} AND p.Estado={PropuestaEstados.Aceptado}
              AND EXISTS (
                SELECT 1 FROM dbo.PropuestaDocuSign d
                WHERE d.PropuestaId=p.PropuestaId AND d.EsVigente=1 AND d.EstadoSobre='completed'
                  AND d.FirmasRequeridas IN (1,2,3)
                  AND (SELECT COUNT(*) FROM dbo.PropuestaDocuSignFirmante f
                       WHERE f.PropuestaDocuSignId=d.PropuestaDocuSignId)=d.FirmasRequeridas
                  AND (SELECT COUNT(*) FROM dbo.PropuestaDocuSignFirmante f
                       WHERE f.PropuestaDocuSignId=d.PropuestaDocuSignId AND f.EstadoFirmante='completed')=d.FirmasRequeridas)
            """, cancellationToken);
        if (changed > 0) return MarcarPropuestaIngresadaStatus.Success;
        // Una segunda solicitud concurrente puede haber realizado la transicion.
        return await _appContext.Propuestas.AsNoTracking()
            .AnyAsync(p => p.PropuestaId == propuestaId && p.Estado == PropuestaEstados.Ingresado, cancellationToken)
            ? MarcarPropuestaIngresadaStatus.Success : MarcarPropuestaIngresadaStatus.NotSigned;
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

        var firmas = new Dictionary<int, PropuestaFirmaSeguimiento>();
        foreach (var ids in proposals.Keys.Chunk(500))
        {
            var batch = await _appContext.Database.SqlQuery<PropuestaFirmaSeguimiento>($"""
                SELECT d.PropuestaId,d.EstadoSobre,CAST(d.FirmasRequeridas AS int) AS FirmasRequeridas,
                    (SELECT COUNT(*) FROM dbo.PropuestaDocuSignFirmante f
                     WHERE f.PropuestaDocuSignId=d.PropuestaDocuSignId AND f.EstadoFirmante='completed') AS FirmasCompletadas,
                    (SELECT COUNT(*) FROM dbo.PropuestaDocuSignFirmante f
                     WHERE f.PropuestaDocuSignId=d.PropuestaDocuSignId) AS TotalFirmantes
                FROM dbo.PropuestaDocuSign d WHERE d.EsVigente=1
                """).Where(f => ids.Contains(f.PropuestaId)).ToListAsync(cancellationToken);
            foreach (var firma in batch) firmas[firma.PropuestaId] = firma;
        }
        return new(true, proposals.Values.ToList(), firmas);
    }
}
