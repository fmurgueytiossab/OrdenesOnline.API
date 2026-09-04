using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;

namespace OrdenesOnline.Infrastructure.Repositories;

public sealed class RepresentanteClientScopeRepository : IRepresentanteClientScopeRepository
{
    private readonly AppDbContext _appContext;
    private readonly OpersabDbContext _opersabContext;

    public RepresentanteClientScopeRepository(
        AppDbContext appContext,
        OpersabDbContext opersabContext)
    {
        _appContext = appContext;
        _opersabContext = opersabContext;
    }

    public async Task<RepresentanteClientScope> GetAsync(
        int representanteId,
        CancellationToken cancellationToken = default)
    {
        var representanteExiste = await _appContext.Representantes
            .AsNoTracking()
            .AnyAsync(
                representante => representante.RepresentanteId == representanteId,
                cancellationToken);

        if (!representanteExiste)
        {
            return new RepresentanteClientScope(false, [], []);
        }

        var seedClientCodes = (await _appContext.CodeRepresentantes
                .AsNoTracking()
                .Where(code => code.RepresentanteId == representanteId)
                .Select(code => code.Cosabcli)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(Normalize)
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (seedClientCodes.Count == 0)
        {
            return new RepresentanteClientScope(true, [], []);
        }

        var gestores = (await _opersabContext.Clientes
                .AsNoTracking()
                .Where(cliente => seedClientCodes.Contains(cliente.Cosabcli))
                .Where(cliente => cliente.Gestor != null && cliente.Gestor != string.Empty)
                .Select(cliente => cliente.Gestor!)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(Normalize)
            .Where(gestor => gestor.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (gestores.Count == 0)
        {
            return new RepresentanteClientScope(true, [], []);
        }

        var clientCodes = (await _opersabContext.Clientes
                .AsNoTracking()
                .Where(cliente =>
                    cliente.Estado != "9" &&
                    cliente.Gestor != null &&
                    gestores.Contains(cliente.Gestor))
                .Select(cliente => cliente.Cosabcli)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(Normalize)
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RepresentanteClientScope(true, gestores, clientCodes);
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
