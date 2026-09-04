using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Tests.TestDoubles;

namespace OrdenesOnline.Tests;

public sealed class ClienteServiceTests
{
    [Theory]
    [InlineData("S")]
    [InlineData("s")]
    [InlineData(" M ")]
    public void JointAccountFlag_SupportsSAndM(string value)
    {
        Assert.True(Cliente.IsJointAccount(value));
    }

    [Fact]
    public void SearchResult_BuildsFullNameAndConvertsEmailcliToArray()
    {
        var result = ClienteSearchResult.Create(
            "C001",
            " Miguel Angel ",
            "Gutierrez",
            null,
            "DESCRIPCION QUE NO DEBE USARSE",
            "deasdaz@yahoo.com; lafdgsgs@hotmail.com;DEASDAZ@yahoo.com;",
            " 999888777 ",
            false);

        Assert.Equal("C001", result.Cosabcli);
        Assert.Equal("Miguel Angel Gutierrez", result.NombreCompleto);
        Assert.Equal(["deasdaz@yahoo.com", "lafdgsgs@hotmail.com"], result.Emails);
        Assert.Equal(["999888777"], result.Nucel);
        Assert.Null(result.BloqueoMotivo);
    }

    [Fact]
    public void SearchResult_ForJointAccount_UsesRepresentativeMobileNumbers()
    {
        var result = ClienteSearchResult.Create(
            "C002",
            "Empresa",
            null,
            null,
            "CUENTA JUAN PEREZ Y MARIA LOPEZ",
            null,
            "999999999",
            true,
            ["111111111", " 222222222 ", "111111111", null],
            " Documentación pendiente ");

        Assert.Equal("CUENTA JUAN PEREZ Y MARIA LOPEZ", result.NombreCompleto);
        Assert.Equal(["111111111", "222222222"], result.Nucel);
        Assert.Equal("Documentación pendiente", result.BloqueoMotivo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mi")]
    public async Task Search_WithLessThanThreeCharacters_DoesNotQueryRepository(string? search)
    {
        var clienteRepository = new FakeClienteRepository();
        var service = new ClienteService(
            clienteRepository,
            new FakeRepresentanteClientScopeRepository());

        var result = await service.Search(7, search);

        Assert.Equal(ClienteSearchStatus.InvalidSearch, result.Status);
        Assert.Equal(0, clienteRepository.SearchCalls);
    }

    [Fact]
    public async Task Search_NormalizesWhitespaceAndLimitsResults()
    {
        var clienteRepository = new FakeClienteRepository();
        var service = new ClienteService(
            clienteRepository,
            new FakeRepresentanteClientScopeRepository());

        var result = await service.Search(7, "  miguel   gutierrez  ", 200);

        Assert.Equal(ClienteSearchStatus.Success, result.Status);
        Assert.Equal("miguel gutierrez", clienteRepository.Search);
        Assert.Equal(ClienteService.MaximumResultLimit, clienteRepository.Take);
        Assert.Equal(["000905"], clienteRepository.Gestores);
    }

    [Fact]
    public async Task Search_WhenRepresentativeDoesNotExist_DoesNotQueryClients()
    {
        var clienteRepository = new FakeClienteRepository();
        var service = new ClienteService(
            clienteRepository,
            new FakeRepresentanteClientScopeRepository(representanteExiste: false));

        var result = await service.Search(999, "miguel");

        Assert.Equal(ClienteSearchStatus.RepresentanteNotFound, result.Status);
        Assert.Equal(0, clienteRepository.SearchCalls);
    }

    private sealed class FakeClienteRepository : IClienteRepository
    {
        public int SearchCalls { get; private set; }
        public string? Search { get; private set; }
        public int Take { get; private set; }
        public IReadOnlyList<string>? Gestores { get; private set; }

        public Task<IReadOnlyList<ClienteSearchResult>> SearchAsync(
            string search,
            int take,
            IReadOnlyList<string> gestores,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            Search = search;
            Take = take;
            Gestores = gestores;
            return Task.FromResult<IReadOnlyList<ClienteSearchResult>>([]);
        }
    }
}
