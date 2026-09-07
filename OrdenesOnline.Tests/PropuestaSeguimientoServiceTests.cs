using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Tests;

public sealed class PropuestaSeguimientoServiceTests
{
    [Theory]
    [InlineData("BVL", "BVL")]
    [InlineData("Local", "BVL")]
    [InlineData("01", "BVL")]
    [InlineData("98", "CANACCORD")]
    [InlineData("Canaccord Renta4", "CANACCORD")]
    [InlineData("Canaccord", "CANACCORD")]
    [InlineData("16", "EUROCLEAR")]
    [InlineData("Euroclear", "EUROCLEAR")]
    [InlineData("Extranjero", "EXTRANJERO")]
    public async Task ShowsDatawebProposalWithoutExternalOperationAsPending(string market, string channel)
    {
        var proposal = Proposal(1, market);
        proposal.Estado = PropuestaEstados.Aceptado;
        var result = await Service(proposal).Get(7, 1, 20);
        var item = Assert.Single(result.Page!.Items);
        Assert.Equal(channel, item.Mercado);
        Assert.Equal("PENDIENTE", item.Estado);
        Assert.Equal(0m, item.CantidadEjecutada);
        Assert.Equal(0m, item.CantidadAnulada);
        Assert.Equal(proposal.Cantidad, item.CantidadPendiente);
        Assert.Equal(string.Empty, item.NumeroOperacion);
        Assert.Equal(proposal.PropuestaId, item.CodigoOrden);
    }

    [Fact]
    public async Task OnlyIncludesTodayInPeruWithStablePagination()
    {
        var (start, end) = PropuestaTrackingDay.GetUtcRange(DateTimeOffset.UtcNow);
        var first = Proposal(1, "BVL"); first.FechaRegistro = start;
        var second = Proposal(2, "Euroclear"); second.FechaRegistro = start;
        var yesterday = Proposal(3, "BVL"); yesterday.FechaRegistro = start.AddTicks(-1);
        var tomorrow = Proposal(4, "BVL"); tomorrow.FechaRegistro = end;
        var service = Service(first, second, yesterday, tomorrow);
        var result = await service.Get(7, 1, 1);
        Assert.Equal(2, result.Page!.TotalCount);
        Assert.Equal(2, Assert.Single(result.Page.Items).CodigoOrden);
        Assert.Equal(1, Assert.Single((await service.Get(7, 2, 1)).Page!.Items).CodigoOrden);
        Assert.Empty((await service.Get(7, int.MaxValue, 100)).Page!.Items);
    }

    [Fact]
    public void PeruDayDoesNotChangeAtUtcMidnight()
    {
        var range = PropuestaTrackingDay.GetUtcRange(DateTimeOffset.Parse("2026-09-08T04:59:59Z"));
        Assert.Equal(DateTime.Parse("2026-09-07T05:00:00Z").ToUniversalTime(), range.StartUtc);
        Assert.Equal(range.StartUtc.AddDays(1), range.EndUtc);
        var next = PropuestaTrackingDay.GetUtcRange(DateTimeOffset.Parse("2026-09-08T05:00:00Z"));
        Assert.Equal(range.EndUtc, next.StartUtc);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task RejectsInvalidPagination(int page, int size) =>
        Assert.Equal(PropuestaSeguimientoStatus.InvalidPagination, (await Service().Get(7, page, size)).Status);

    [Fact]
    public async Task RejectsMissingRepresentative()
    {
        var service = new PropuestaSeguimientoService(new FakeRepository(new(false, [])));
        Assert.Equal(PropuestaSeguimientoStatus.RepresentanteNotFound, (await service.Get(7, 1, 20)).Status);
    }

    private static Propuesta Proposal(int id, string market) => new()
    {
        PropuestaId = id, Mercado = market, Cosabcli = "C001", Tipo = "Compra",
        Instrumento = "ABC", Cantidad = 100, Precio = 12.5m, FechaRegistro = DateTime.UtcNow
    };
    private static PropuestaSeguimientoService Service(params Propuesta[] proposals) =>
        new(new FakeRepository(new(true, proposals)));
    private sealed class FakeRepository(PropuestaSeguimientoSnapshot snapshot) : IPropuestaSeguimientoRepository
    {
        public Task<PropuestaSeguimientoSnapshot> GetAsync(int id, CancellationToken token = default) =>
            Task.FromResult(snapshot);
    }
}
