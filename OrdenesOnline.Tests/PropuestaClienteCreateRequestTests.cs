using System.ComponentModel.DataAnnotations;
using OrdenesOnline.Domain.DTO;

namespace OrdenesOnline.Tests;

public sealed class PropuestaClienteCreateRequestTests
{
    [Theory]
    [InlineData("BVL")]
    [InlineData("Canaccord Renta4")]
    [InlineData("Pershing")]
    public void Validation_AcceptsSupportedMarkets(string market)
    {
        var results = Validate(CreateRequest(market));

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("Local")]
    [InlineData("Extranjero")]
    [InlineData("Otro")]
    public void Validation_RejectsUnsupportedMarkets(string market)
    {
        var results = Validate(CreateRequest(market));

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(PropuestaClienteCreateRequest.Mercado)));
    }

    [Fact]
    public void Validation_RejectsInvalidClientEmail()
    {
        var request = CreateRequest("BVL");
        request.CorreoCliente = "correo-invalido";

        var results = Validate(request);

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(PropuestaClienteCreateRequest.CorreoCliente)));
    }

    private static List<ValidationResult> Validate(PropuestaClienteCreateRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        return results;
    }

    private static PropuestaClienteCreateRequest CreateRequest(string market) => new()
    {
        CorreoCliente = "cliente@example.com",
        Cosabcli = "C001",
        Tipo = "Compra",
        TipoOrden = "Mercado",
        Cantidad = 10,
        Instrumento = "ABC",
        Mercado = market,
        Moneda = "USD",
        Vigencia = "Día"
    };
}
