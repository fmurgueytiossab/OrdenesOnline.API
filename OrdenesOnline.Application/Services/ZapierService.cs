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
            "https://hooks.zapier.com/hooks/catch/25114517/urvh202/"; // tu URL real

        public ZapierService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task EnviarPropuestaCreada(Propuesta propuesta)
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
                precio = propuesta.Precio,
                mercado = propuesta.Mercado,
                fecha = DateTime.UtcNow
            };

            await _httpClient.PostAsJsonAsync(ZapierWebhookUrl, payload);
        }
    }
}
