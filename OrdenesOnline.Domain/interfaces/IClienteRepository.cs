using OrdenesOnline.Domain.DTO;

namespace OrdenesOnline.Domain.interfaces;

public interface IClienteRepository
{
    Task<IReadOnlyList<ClienteSearchResult>> SearchAsync(
        string search,
        int take,
        IReadOnlyList<string> gestores,
        CancellationToken cancellationToken = default);
}
