using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Tests;

public sealed class PropuestaBvlSeguimientoServiceTests
{
    [Fact]
    public async Task Get_MatchesDatawebProposalAndReturnsBvlExecutionStatus()
    {
        var propuesta = CreatePropuesta(41, "C001", "Compra", 100, "ABC", 12.50m);
        var operacion = CreateOperacion(
            "C001",
            "C",
            100,
            "ABC",
            12.50m,
            ejecutada: 100,
            anulada: 0);
        var service = CreateService([propuesta], [operacion]);

        var result = await service.Get(7, 1, 20);

        Assert.Equal(PropuestaBvlSeguimientoStatus.Success, result.Status);
        var item = Assert.Single(result.Page!.Items);
        Assert.Equal(41, item.CodigoOrden);
        Assert.Equal("9001", item.NumeroOperacion);
        Assert.Equal("C", item.Tipo);
        Assert.Equal("EJECUTADA", item.Estado);
        Assert.Equal(0, item.CantidadPendiente);
    }

    [Fact]
    public async Task Get_WhenPartExecutedAndRemainderCancelled_ReturnsPartialWithNoPendingQuantity()
    {
        var propuesta = CreatePropuesta(42, "C001", "Venta", 100, "XYZ", 8m);
        var operacion = CreateOperacion(
            "C001",
            "V",
            100,
            "XYZ",
            8m,
            ejecutada: 20,
            anulada: 80);
        var service = CreateService([propuesta], [operacion]);

        var result = await service.Get(7, 1, 20);

        var item = Assert.Single(result.Page!.Items);
        Assert.Equal("PARCIAL", item.Estado);
        Assert.Equal(20, item.CantidadEjecutada);
        Assert.Equal(80, item.CantidadAnulada);
        Assert.Equal(0, item.CantidadPendiente);
    }

    [Theory]
    [InlineData(0, 100, "ANULADA")]
    [InlineData(20, 0, "PARCIAL")]
    [InlineData(0, 0, "PENDIENTE")]
    public async Task Get_MapsRemainingBvlStatuses(
        int ejecutada,
        int anulada,
        string expectedStatus)
    {
        var propuesta = CreatePropuesta(44, "C001", "Compra", 100, "ABC", 12.50m);
        var operacion = CreateOperacion(
            "C001",
            "C",
            100,
            "ABC",
            12.50m,
            ejecutada,
            anulada);
        var service = CreateService([propuesta], [operacion]);

        var result = await service.Get(7, 1, 20);

        Assert.Equal(expectedStatus, Assert.Single(result.Page!.Items).Estado);
    }

    [Fact]
    public async Task Get_DoesNotMatchAnotherClientOrTradeSide()
    {
        var propuesta = CreatePropuesta(43, "C001", "Compra", 100, "ABC", 12.50m);
        var otherClient = CreateOperacion("C002", "C", 100, "ABC", 12.50m, 0, 0);
        var otherSide = CreateOperacion("C001", "V", 100, "ABC", 12.50m, 0, 0);
        var service = CreateService([propuesta], [otherClient, otherSide]);

        var result = await service.Get(7, 1, 20);

        Assert.Empty(result.Page!.Items);
        Assert.Equal(0, result.Page.TotalCount);
    }

    [Fact]
    public async Task Get_DoesNotMatchAnOperationFromAnotherDate()
    {
        var propuesta = CreatePropuesta(45, "C001", "Compra", 100, "ABC", 12.50m);
        propuesta.FechaRegistro = new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc);
        var previousDayOperation = CreateOperacion("C001", "C", 100, "ABC", 12.50m, 0, 0);
        var service = CreateService([propuesta], [previousDayOperation]);

        var result = await service.Get(7, 1, 20);

        Assert.Empty(result.Page!.Items);
        Assert.Equal(0, result.Page.TotalCount);
    }

    [Fact]
    public async Task Get_WithSeveralMatchingOperations_ReturnsAtMostOneItemPerDatawebProposal()
    {
        var propuesta = CreatePropuesta(46, "C001", "Compra", 100, "ABC", 12.50m);
        var firstOperation = CreateOperacion("C001", "C", 100, "ABC", 12.50m, 20, 0);
        var secondOperation = CreateOperacion("C001", "C", 100, "ABC", 12.50m, 100, 0);
        var service = CreateService([propuesta], [firstOperation, secondOperation]);

        var result = await service.Get(7, 1, 20);

        Assert.Single(result.Page!.Items);
        Assert.Equal(1, result.Page.TotalCount);
    }

    [Theory]
    [InlineData("Canaccord Renta4", "57", "CANACCORD")]
    [InlineData("Viewtrade", "66", "VIEWTRADE")]
    public async Task Get_MatchesExternalProposalAndMapsBrokerChannel(
        string mercado,
        string brokerCode,
        string expectedChannel)
    {
        var propuesta = CreatePropuesta(47, "C001", "Venta", 4000, "CPAC", null, mercado);
        var operacion = CreateOperacionExterior(
            brokerCode,
            "EXT-1001",
            "C001",
            "V",
            4000,
            "CPAC",
            18.25m);
        var service = CreateService([propuesta], [], [operacion]);

        var result = await service.Get(7, 1, 20);

        var item = Assert.Single(result.Page!.Items);
        Assert.Equal(47, item.CodigoOrden);
        Assert.Equal("EXT-1001", item.NumeroOperacion);
        Assert.Equal(expectedChannel, item.Mercado);
        Assert.Equal("PENDIENTE", item.Estado);
        Assert.Equal(4000, item.CantidadPendiente);
    }

    [Fact]
    public async Task Get_DoesNotMatchAnExternalProposalWithAnotherBroker()
    {
        var propuesta = CreatePropuesta(
            48,
            "C001",
            "Compra",
            100,
            "ABC",
            10m,
            "Canaccord");
        var viewtradeOperation = CreateOperacionExterior(
            "66",
            "EXT-1002",
            "C001",
            "C",
            100,
            "ABC",
            10m);
        var service = CreateService([propuesta], [], [viewtradeOperation]);

        var result = await service.Get(7, 1, 20);

        Assert.Empty(result.Page!.Items);
    }

    [Fact]
    public async Task Get_WhenRepresentativeDoesNotExist_ReturnsNotFoundStatus()
    {
        var service = new PropuestaBvlSeguimientoService(
            new FakeRepository(new PropuestaBvlSeguimientoSnapshot(false, [], [], [])));

        var result = await service.Get(999, 1, 20);

        Assert.Equal(PropuestaBvlSeguimientoStatus.RepresentanteNotFound, result.Status);
        Assert.Null(result.Page);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Get_WithInvalidPagination_ReturnsInvalidPagination(int page, int pageSize)
    {
        var service = CreateService([], []);

        var result = await service.Get(7, page, pageSize);

        Assert.Equal(PropuestaBvlSeguimientoStatus.InvalidPagination, result.Status);
    }

    private static PropuestaBvlSeguimientoService CreateService(
        IReadOnlyList<Propuesta> propuestas,
        IReadOnlyList<OperacionBvl> operaciones,
        IReadOnlyList<OperacionExterior>? operacionesExterior = null) =>
        new(new FakeRepository(new PropuestaBvlSeguimientoSnapshot(
            true,
            propuestas,
            operaciones,
            operacionesExterior ?? [])));

    private static Propuesta CreatePropuesta(
        int id,
        string cosabcli,
        string tipo,
        int cantidad,
        string instrumento,
        decimal? precio,
        string mercado = "BVL") => new()
    {
        PropuestaId = id,
        NombreOperador = "Representante",
        CorreoCorporativo = "representante@example.com",
        Cosabcli = cosabcli,
        Tipo = tipo,
        TipoOrden = "Límite",
        Cantidad = cantidad,
        Instrumento = instrumento,
        Precio = precio,
        Vigencia = "Día",
        Mercado = mercado,
        FechaRegistro = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc)
    };

    private static OperacionBvl CreateOperacion(
        string cosabcli,
        string tipo,
        decimal cantidad,
        string instrumento,
        decimal? precio,
        decimal ejecutada,
        decimal anulada) => new(
        cosabcli,
        new DateOnly(2026, 9, 4),
        new TimeOnly(10, 30),
        "9001",
        instrumento,
        ejecutada,
        anulada,
        tipo,
        cantidad,
        precio);

    private static OperacionExterior CreateOperacionExterior(
        string brokerCode,
        string numeroOperacion,
        string cosabcli,
        string tipo,
        decimal cantidad,
        string instrumento,
        decimal? precio) => new(
        brokerCode,
        numeroOperacion,
        new DateOnly(2026, 9, 4),
        instrumento,
        tipo,
        cantidad,
        precio,
        cosabcli);

    private sealed class FakeRepository : IPropuestaBvlSeguimientoRepository
    {
        private readonly PropuestaBvlSeguimientoSnapshot _snapshot;

        public FakeRepository(PropuestaBvlSeguimientoSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<PropuestaBvlSeguimientoSnapshot> GetAsync(
            int representanteId,
            CancellationToken cancellationToken = default) => Task.FromResult(_snapshot);
    }
}
