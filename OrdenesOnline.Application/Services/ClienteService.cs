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

    public ClienteService(IClienteRepository clienteRepository)
    {
        _clienteRepository = clienteRepository;
    }

    public async Task<ClienteSearchServiceResult> Search(
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

        var effectiveLimit = Math.Clamp(limit, 1, MaximumResultLimit);
        var clients = await _clienteRepository.SearchAsync(
            normalizedSearch,
            effectiveLimit,
            cancellationToken);

        return new ClienteSearchServiceResult(ClienteSearchStatus.Success, clients);
    }
}

public enum ClienteSearchStatus
{
    Success,
    InvalidSearch
}

public sealed record ClienteSearchServiceResult(
    ClienteSearchStatus Status,
    IReadOnlyList<ClienteSearchResult>? Items = null);
