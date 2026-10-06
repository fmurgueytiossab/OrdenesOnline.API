using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class DocusignTemplateTest
{
    private static readonly Dictionary<string, string> Values = new()
    {
        ["Operacion"] = "Compra", ["Valor"] = "PRUEBA", ["Cantidad"] = "10",
        ["TipoOrden"] = "Limite", ["Precio"] = "1.00", ["Moneda"] = "USD",
        ["Monto"] = "10.00", ["Vigencia"] = "Hoy", ["Mercado"] = "BVL",
        ["NombreRepresentante"] = "Representante de prueba", ["CodigoCliente"] = "PRUEBA"
    };

    public static async Task<int> Send(HttpClient client, string root)
    {
        Console.WriteLine("Prueba en desarrollo con datos ficticios; usa numeros de prueba autorizados.");
        Console.Write("Caso: 1 = individual, 2 = mancomunada (ambos deben firmar): ");
        var option = Console.ReadLine()?.Trim();
        if (option is not ("1" or "2"))
        {
            Console.WriteLine("Selecciona 1 o 2 y vuelve a ejecutar la prueba.");
            return 1;
        }
        var templateId = option == "1" ? "a412ccfb-74da-4a7d-b831-a3c354f10c41"
            : "10c7cb91-971e-4d02-aef9-54c5dea3a270";
        string[] roles = option == "1" ? ["Cliente"] : ["Titular1", "Titular2"];
        using var recipients = await GetJson(client, $"{root}/templates/{templateId}/recipients");
        if (recipients is null) return 1;
        // Evita enviar por accidente a destinatarios adicionales guardados en la plantilla.
        foreach (var property in recipients.RootElement.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Array && property.Name != "signers"
                && property.Value.GetArrayLength() > 0)
            {
                Console.WriteLine("La plantilla tiene destinatarios adicionales. Revisa la plantilla antes de probar.");
                return 1;
            }
        var templateSigners = recipients.RootElement.GetProperty("signers").EnumerateArray().ToArray();
        if (templateSigners.Length != roles.Length
            || roles.Any(role => templateSigners.Count(s => String(s, "roleName") == role) != 1))
        {
            Console.WriteLine("Las funciones de la plantilla no coinciden con las esperadas: " + string.Join(", ", roles));
            return 1;
        }

        var signers = new List<object>();
        var foundLabels = new HashSet<string>(StringComparer.Ordinal);
        var usedPhones = new HashSet<string>();
        foreach (var role in roles)
        {
            var signer = templateSigners.Single(s => String(s, "roleName") == role);
            var recipientId = String(signer, "recipientId");
            if (!string.IsNullOrEmpty(String(signer, "clientUserId")))
            {
                Console.WriteLine("La plantilla tiene firma incrustada. Usa destinatarios de firma remota para WhatsApp.");
                return 1;
            }
            using var tabs = await GetJson(client,
                $"{root}/templates/{templateId}/recipients/{Uri.EscapeDataString(recipientId)}/tabs");
            if (tabs is null) return 1;
            if (!tabs.RootElement.TryGetProperty("signHereTabs", out var signatures)
                || signatures.GetArrayLength() == 0
                || signatures.EnumerateArray().All(t => String(t, "optional").Equals("true", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"La funcion {role} necesita al menos una firma obligatoria.");
                return 1;
            }
            var textTabs = new List<object>();
            if (tabs.RootElement.TryGetProperty("textTabs", out var textFields))
                foreach (var field in textFields.EnumerateArray())
                {
                    var label = String(field, "tabLabel");
                    if (!Values.TryGetValue(label, out var value))
                    {
                        Console.WriteLine("Hay un campo de texto con etiqueta inesperada. Revisa las etiquetas de datos de la plantilla.");
                        return 1;
                    }
                    foundLabels.Add(label);
                    textTabs.Add(new { tabLabel = label, value, locked = "true", required = "false" });
                }
            var name = Read($"Nombre del firmante para {role}");
            if (name.Length > 100) { Console.WriteLine("El nombre debe tener como maximo 100 caracteres."); return 1; }
            var country = Read("Codigo de pais sin + (Peru: 51)");
            var number = Read("Celular sin codigo de pais, espacios ni guiones");
            if (!Regex.IsMatch(country, "^[1-9][0-9]{0,2}$")
                || !Regex.IsMatch(number, "^[0-9]{6,14}$") || country.Length + number.Length > 15
                || (country == "51" && !Regex.IsMatch(number, "^9[0-9]{8}$")))
            {
                Console.WriteLine("Numero invalido. Para Peru usa 51 y un celular de nueve digitos que empiece en 9.");
                return 1;
            }
            if (!usedPhones.Add(country + number))
            {
                Console.WriteLine("Para esta prueba mancomunada indica dos celulares distintos.");
                return 1;
            }
            signers.Add(new
            {
                roleName = role, recipientId, name, email = "", routingOrder = "1",
                deliveryMethod = "WhatsApp", phoneNumber = new { countryCode = country, number },
                tabs = new { textTabs }
            });
        }
        var missing = Values.Keys.Where(label => !foundLabels.Contains(label)).ToArray();
        if (missing.Length > 0)
        {
            Console.WriteLine("Faltan etiquetas en la plantilla: " + string.Join(", ", missing));
            return 1;
        }
        Console.WriteLine($"Plantilla verificada. Se enviara UN sobre a {roles.Length} firmante(s) por WhatsApp.");
        Console.WriteLine("Cada titular recibira su propio enlace al mismo documento. En mancomunada se requieren ambas firmas.");
        Console.Write("Escribe ENVIAR para realizar el envio de prueba; cualquier otra respuesta cancela: ");
        if (Console.ReadLine()?.Trim() != "ENVIAR")
        {
            Console.WriteLine("Prueba cancelada. No se creo ningun sobre.");
            return 0;
        }
        // Un solo POST: nunca se reintenta automaticamente un envio ante errores de red.
        var request = new
        {
            emailSubject = "PRUEBA - Autorizacion de propuesta de orden",
            emailBlurb = "Documento de prueba con datos ficticios. No corresponde a una orden real.",
            status = "sent",
            compositeTemplates = new[] { new
            {
                compositeTemplateId = "1",
                serverTemplates = new[] { new { sequence = "1", templateId } },
                inlineTemplates = new[] { new { sequence = "2", recipients = new { signers } } }
            } }
        };
        try
        {
            using var response = await client.PostAsJsonAsync($"{root}/envelopes", request);
            using var result = await ReadResponse(response);
            if (result is null) return 1;
            var id = result.RootElement.GetProperty("envelopeId").GetString();
            Console.WriteLine($"SOBRE CREADO: {id}");
            Console.WriteLine("DocuSign acepto el envio. Esto no confirma la entrega de WhatsApp ni las firmas.");
            Console.WriteLine($"Despues de firmar, consulta: dotnet run --project .\\tools\\DocusignAuthCheck\\DocusignAuthCheck.csproj -- --status {id}");
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.WriteLine("No se pudo confirmar la respuesta del envio. Revisa Acuerdos en DocuSign antes de repetir para evitar duplicados.");
            return 1;
        }
    }

    public static async Task<int> ShowStatus(HttpClient client, string root, string id)
    {
        using var envelope = await GetJson(client, $"{root}/envelopes/{id}");
        if (envelope is null) return 1;
        var status = String(envelope.RootElement, "status");
        Console.WriteLine($"Estado del sobre: {status}");
        Console.WriteLine(status switch
        {
            "completed" => "Todas las acciones requeridas del sobre estan completadas.",
            "declined" => "El sobre fue rechazado.",
            "voided" => "El sobre fue anulado; no equivale a una firma completada.",
            _ => "El sobre todavia no esta completado. No se debe aceptar la propuesta."
        });
        using var recipients = await GetJson(client, $"{root}/envelopes/{id}/recipients");
        if (recipients is null) return 1;
        foreach (var signer in recipients.RootElement.GetProperty("signers").EnumerateArray())
            Console.WriteLine($"Firmante {String(signer, "recipientId")}: {String(signer, "status")}");
        return 0;
    }

    private static async Task<JsonDocument?> GetJson(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        return await ReadResponse(response);
    }

    private static async Task<JsonDocument?> ReadResponse(HttpResponseMessage response)
    {
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (response.IsSuccessStatusCode) return json;
        var code = String(json.RootElement, "errorCode");
        Console.WriteLine($"DocuSign rechazo la operacion (HTTP {(int)response.StatusCode}).");
        // Solo codigos; los mensajes remotos pueden contener datos de destinatarios.
        if (Regex.IsMatch(code, "^[A-Z0-9_]{1,100}$")) Console.WriteLine($"Codigo: {code}");
        json.Dispose();
        return null;
    }

    private static string String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ToString() : "";

    private static string Read(string label)
    {
        Console.Write($"{label}: ");
        var value = Console.ReadLine()?.Trim();
        return !string.IsNullOrEmpty(value) ? value : throw new InvalidOperationException();
    }
}
