using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Tests.TestDoubles;

internal sealed class FakeRepresentanteClientScopeRepository : IRepresentanteClientScopeRepository
{
    private readonly RepresentanteClientScope _scope;

    public FakeRepresentanteClientScopeRepository(
        bool representanteExiste = true,
        IReadOnlyList<string>? gestores = null,
        IReadOnlyList<string>? clientCodes = null)
    {
        _scope = new RepresentanteClientScope(
            representanteExiste,
            gestores ?? ["000905"],
            clientCodes ?? ["C001"]);
    }

    public Task<RepresentanteClientScope> GetAsync(
        int representanteId,
        CancellationToken cancellationToken = default) => Task.FromResult(_scope);
}
