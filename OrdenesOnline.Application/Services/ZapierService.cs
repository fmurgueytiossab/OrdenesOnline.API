using Microsoft.Extensions.Configuration;
using OrdenesOnline.Domain.entities;
using System.Net.Http.Json;

namespace OrdenesOnline.Application.Services;

public sealed class ZapierService
{
    private readonly HttpClient _httpClient;
    private readonly Uri? _webhookUri;

    public ZapierService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        var webhookUrl = configuration["App:ZapierWebhookUrl"];
        _webhookUri = Uri.TryCreate(webhookUrl, UriKind.Absolute, out var webhookUri)
            ? webhookUri
            : null;
    }

    public async Task EnviarPropuestaCreada(
        Propuesta propuesta,
        string dni,
        string moneda,
        CancellationToken cancellationToken = default)
    {
        if (_webhookUri is null)
        {
            throw new ZapierConfigurationException(
                "Falta una App:ZapierWebhookUrl válida.");
        }

        var payload = new
        {
            propuestaId = propuesta.PropuestaId,
            nombreOperador = propuesta.NombreOperador,
            correo = propuesta.CorreoCorporativo,
            cosabcli = propuesta.Cosabcli,
            tipo = propuesta.Tipo,
            cantidad = propuesta.Cantidad,
            instrumento = propuesta.Instrumento,
            tipoOrden = propuesta.TipoOrden,
            precio = propuesta.TipoOrden.Equals("Mercado", StringComparison.OrdinalIgnoreCase)
                ? propuesta.TipoOrden
                : propuesta.Precio?.ToString(),
            mercado = propuesta.Mercado,
            moneda,
            fecha = DateTime.UtcNow,
            monto = propuesta.TipoOrden.Equals("Mercado", StringComparison.OrdinalIgnoreCase) && propuesta.Cantidad > 0
                ? "No aplica"
                : propuesta.Monto?.ToString(),
            dni,
            vigencia = propuesta.Vigencia
        };

        using var response = await _httpClient.PostAsJsonAsync(_webhookUri, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class ZapierConfigurationException(string message) : Exception(message);
