using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

public sealed class ClienteService
{
    public const int MinimumSearchLength = 3;
    public const int MaximumSearchLength = 100;
    public const int DefaultResultLimit = 20;
    public const int MaximumResultLimit = 50;

    private readonly IClienteRepository _clienteRepository;
    private readonly IRepresentanteClientScopeRepository _clientScopeRepository;

    public ClienteService(
        IClienteRepository clienteRepository,
        IRepresentanteClientScopeRepository clientScopeRepository)
    {
        _clienteRepository = clienteRepository;
        _clientScopeRepository = clientScopeRepository;
    }

    public async Task<ClienteSearchServiceResult> Search(
        int representanteId,
        string? search,
        int limit = DefaultResultLimit,
        CancellationToken cancellationToken = default)
    {
       var normalizedSearch = string.Join(
            ' ',
            (search ?? string.Empty).Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (normalizedSearch.Length < MinimumSearchLength ||
            normalizedSearch.Length > MaximumSearchLength)
        {
            return new ClienteSearchServiceResult(ClienteSearchStatus.InvalidSearch);
        }

        var clientScope = await _clientScopeRepository.GetAsync(
            representanteId,
            cancellationToken);
        if (!clientScope.RepresentanteExiste)
        {
            return new ClienteSearchServiceResult(ClienteSearchStatus.RepresentanteNotFound);
        }

        if (clientScope.Gestores.Count == 0)
        {
            return new ClienteSearchServiceResult(ClienteSearchStatus.Success, []);
        }

        var effectiveLimit = Math.Clamp(limit, 1, MaximumResultLimit);
        var clients = await _clienteRepository.SearchAsync(
            normalizedSearch,
            effectiveLimit,
            clientScope.Gestores,
            cancellationToken);

        return new ClienteSearchServiceResult(ClienteSearchStatus.Success, clients);
    }
}

public enum ClienteSearchStatus
{
    Success,
    InvalidSearch,
    RepresentanteNotFound
}

public sealed record ClienteSearchServiceResult(
    ClienteSearchStatus Status,
    IReadOnlyList<ClienteSearchResult>? Items = null);
