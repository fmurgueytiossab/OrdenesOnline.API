using System.ComponentModel.DataAnnotations;

namespace OrdenesOnline.Domain.DTO;

public sealed class PropuestaClienteCreateRequest : IValidatableObject
{
    private static readonly string[] MercadosPermitidos =
        ["BVL", "Canaccord Renta4", "Pershing"];

    [Required, EmailAddress, StringLength(254)]
    public string CorreoCliente { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string Cosabcli { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string Tipo { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string TipoOrden { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int Cantidad { get; set; }

    [Required, StringLength(50)]
    public string Instrumento { get; set; } = string.Empty;

    public decimal? Precio { get; set; }

    public decimal? Monto { get; set; }

    [Required, StringLength(30)]
    public string Mercado { get; set; } = string.Empty;

    [Required, StringLength(10)]
    public string Moneda { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string Vigencia { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!TryGetCanonicalMarket(Mercado, out _))
        {
            yield return new ValidationResult(
                $"El mercado debe ser uno de los siguientes: {string.Join(", ", MercadosPermitidos)}.",
                [nameof(Mercado)]);
        }

        if (Cantidad <= 0 && (!Monto.HasValue || Monto.Value <= 0))
        {
            yield return new ValidationResult(
                "Debe indicar una cantidad o un monto mayor que cero.",
                [nameof(Cantidad), nameof(Monto)]);
        }

        if (Precio.HasValue && Precio.Value <= 0)
        {
            yield return new ValidationResult(
                "El precio debe ser mayor que cero.",
                [nameof(Precio)]);
        }

        if (Monto.HasValue && Monto.Value <= 0)
        {
            yield return new ValidationResult(
                "El monto debe ser mayor que cero.",
                [nameof(Monto)]);
        }

        if (!string.Equals(TipoOrden, "Mercado", StringComparison.OrdinalIgnoreCase) && !Precio.HasValue)
        {
            yield return new ValidationResult(
                "El precio es obligatorio cuando la orden no es a mercado.",
                [nameof(Precio)]);
        }
    }

    public static bool TryGetCanonicalMarket(string? market, out string canonicalMarket)
    {
        canonicalMarket = MercadosPermitidos.FirstOrDefault(
            allowed => string.Equals(allowed, market?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;

        return canonicalMarket.Length > 0;
    }
}
