using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using System.Net.Http.Json;

namespace OrdenesOnline.Application.Services
{
    public class ZapierService
    {
        private readonly HttpClient _httpClient;
        private const string ZapierWebhookUrl =
            "https://hooks.zapier.com/hooks/catch/25114517/ug8php7/"; // tu URL real

        public ZapierService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task EnviarPropuestaCreada(Propuesta propuesta, string dni, string moneda)
        {
            var payload = new
            {
                propuestaId = propuesta.Id,
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
                monto = propuesta.TipoOrden == "Mercado" ? "No aplica" : (propuesta.Precio * propuesta.Cantidad).ToString(),
                dni = dni,
                vigencia = propuesta.Vigencia
            };

            await _httpClient.PostAsJsonAsync(ZapierWebhookUrl, payload);
        }

    }
}
