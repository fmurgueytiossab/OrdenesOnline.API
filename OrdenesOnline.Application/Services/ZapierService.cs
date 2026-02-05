using OrdenesOnline.Domain.entities;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;

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

        public async Task EnviarPropuestaCreada(Propuesta propuesta, string moneda)
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
                texto = propuesta.Tipo + " " + propuesta.Cantidad + (propuesta.Cantidad != 1 ? " acciones de " : " acción de ") + propuesta.Instrumento +
                        (propuesta.Precio == null ? " a precio de mercado" : " a un precio de " + propuesta.Precio + " cada acción") +
                        " en el mercado " + propuesta.Mercado + "."
            };

            await _httpClient.PostAsJsonAsync(ZapierWebhookUrl, payload);
        }

    }
}
