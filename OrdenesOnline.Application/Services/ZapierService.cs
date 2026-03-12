using Microsoft.Extensions.Configuration;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using System.Net.Http.Json;

namespace OrdenesOnline.Application.Services
{
    public class ZapierService
    {
        private readonly HttpClient _httpClient;
        private readonly string _zapierWebhookUrl;        

        public ZapierService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _zapierWebhookUrl = configuration["App:ZapierWebhookUrl"];
        }

        public async Task EnviarPropuestaCreada(Propuesta propuesta, string dni, string moneda)
        {
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
                precio = propuesta.TipoOrden == "Mercado" ? propuesta.TipoOrden : propuesta.Precio.ToString(),
                mercado = propuesta.Mercado,
                moneda = moneda, // 👈 aquí
                fecha = DateTime.UtcNow,
                monto = propuesta.TipoOrden == "Mercado" && propuesta.Cantidad > 0
                        ? "No aplica" : propuesta.TipoOrden == "Mercado" && propuesta.Cantidad == 0
                        ? propuesta.Monto.ToString()
                        : propuesta.Monto.ToString(),
                dni = dni,
                vigencia = propuesta.Vigencia
            };

            await _httpClient.PostAsJsonAsync(_zapierWebhookUrl, payload);

        }

    }
}