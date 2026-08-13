using OrdenesOnline.Domain.DTO;
using System.ComponentModel.DataAnnotations;

namespace OrdenesOnline.Tests;

public sealed class PropuestaCreateRequestTests
{
    [Fact]
    public void LimitOrder_RequiresPrice()
    {
        var request = CreateValidRequest();
        request.TipoOrden = "Límite";
        request.Precio = null;

        var errors = Validate(request);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(request.Precio)));
    }

    [Fact]
    public void MarketOrder_AllowsNoPrice()
    {
        var request = CreateValidRequest();
        request.TipoOrden = "Mercado";
        request.Precio = null;

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Request_RequiresPositiveQuantityOrAmount()
    {
        var request = CreateValidRequest();
        request.Cantidad = 0;
        request.Monto = null;

        var errors = Validate(request);

        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(request.Cantidad)));
    }

    private static PropuestaCreateRequest CreateValidRequest() => new()
    {
        Cosabcli = "C001",
        Tipo = "Compra",
        TipoOrden = "Mercado",
        Cantidad = 10,
        Instrumento = "ABC",
        Mercado = "Local",
        Moneda = "PEN",
        Vigencia = "Día"
    };

    private static IReadOnlyList<ValidationResult> Validate(PropuestaCreateRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        return results;
    }
}
