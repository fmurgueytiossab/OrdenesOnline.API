using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

// Singleton: el JWT se renueva una vez por periodo, nunca se almacena en SQL ni se registra.
public sealed class DocuSignClient(HttpClient http, DocuSignOptions options) : IDisposable
{
    private readonly SemaphoreSlim _auth = new(1, 1);
    private string? _token;
    private string? _root;
    private DateTimeOffset _expires;
    private static readonly string[] CommonLabels = ["TipoOperacion", "Valor", "Cantidad", "Precio",
        "Monto", "Moneda", "Vigencia", "Mercado", "NombreRepresentante"];

    public static IReadOnlyList<string> TemplateLabels(bool joint) => joint
        ? [.. CommonLabels, "NombreCuenta", "CodigoCuenta"]
        : [.. CommonLabels, "NombreCliente", "CodigoCliente"];

    private async Task Authenticate(CancellationToken ct)
    {
        await _auth.WaitAsync(ct);
        try
        {
            if (_token is not null && _expires > DateTimeOffset.UtcNow) return;
            static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var payload = B64(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = options.IntegrationKey, sub = options.UserId, aud = options.AuthServer,
                iat = now, exp = now + 3600, scope = "signature impersonation"
            }));
            using var rsa = RSA.Create();
            rsa.ImportFromPem(await File.ReadAllTextAsync(options.PrivateKeyPath, ct));
            var unsigned = header + "." + payload;
            var assertion = unsigned + "." + B64(rsa.SignData(Encoding.ASCII.GetBytes(unsigned),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            using var authResponse = await http.PostAsync($"https://{options.AuthServer}/oauth/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = assertion
                }), ct);
            await EnsureSuccess(authResponse, ct);
            using var token = JsonDocument.Parse(await authResponse.Content.ReadAsStringAsync(ct));
            var accessToken = token.RootElement.GetProperty("access_token").GetString()!;
            using var userRequest = new HttpRequestMessage(HttpMethod.Get, $"https://{options.AuthServer}/oauth/userinfo");
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var userResponse = await http.SendAsync(userRequest, ct);
            await EnsureSuccess(userResponse, ct);
            using var user = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync(ct));
            var account = user.RootElement.GetProperty("accounts").EnumerateArray()
                .SingleOrDefault(a => String(a, "account_id").Equals(options.AccountId.ToString(), StringComparison.OrdinalIgnoreCase));
            if (account.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException("El usuario de DocuSign no tiene acceso a la cuenta configurada.");
            var baseUri = new Uri(account.GetProperty("base_uri").GetString()!);
            if (baseUri.Scheme != "https" || baseUri.Port != 443 || baseUri.UserInfo.Length != 0
                || (options.Entorno == "Desarrollo" ? baseUri.Host != "demo.docusign.net"
                    : !baseUri.Host.EndsWith(".docusign.net", StringComparison.OrdinalIgnoreCase) || baseUri.Host == "demo.docusign.net"))
                throw new InvalidOperationException("La URL de cuenta DocuSign no corresponde al ambiente configurado.");
            _root = baseUri.GetLeftPart(UriPartial.Authority) + $"/restapi/v2.1/accounts/{options.AccountId}";
            _token = accessToken;
            _expires = DateTimeOffset.UtcNow.AddSeconds(token.RootElement.GetProperty("expires_in").GetInt32() - 120);
        }
        finally { _auth.Release(); }
    }

    public async Task<HttpResponseMessage> Request(HttpMethod method, string path, object? body, CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        await Authenticate(ct);
        using var request = new HttpRequestMessage(method, _root + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body is not null) request.Content = JsonContent.Create(body);
        // Nunca repetir automaticamente POST/PUT: una respuesta perdida no significa que el envio fallo.
        var response = await http.SendAsync(request, completion, ct);
        try { await EnsureSuccess(response, ct); return response; }
        catch { response.Dispose(); throw; }
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string code = "";
        string reason = "";
        try
        {
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (error.RootElement.ValueKind == JsonValueKind.Object)
            {
                static string Identifier(JsonElement root, string property)
                {
                    if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return "";
                    var candidate = value.GetString()!;
                    return candidate.Length is > 0 and <= 100
                        && candidate.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? candidate : "";
                }

                code = Identifier(error.RootElement, "errorCode");
                if (code.Length == 0) code = Identifier(error.RootElement, "error");
                // OAuth devuelve error/error_description; registrar solo identificadores, nunca el cuerpo ni tokens.
                reason = Identifier(error.RootElement, "error_description");
            }
        }
        catch (JsonException) { }
        var detail = string.Join(" ", new[] { code, reason }.Where(value => value.Length > 0));
        throw new HttpRequestException($"DocuSign HTTP {(int)response.StatusCode} {detail}".TrimEnd(), null, response.StatusCode);
    }

    private async Task<JsonDocument> GetJson(string path, CancellationToken ct)
    {
        using var response = await Request(HttpMethod.Get, path, null, ct);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<IReadOnlyList<DocuSignFirmante>> TemplateSigners(DocuSignPreparacion plan, CancellationToken ct)
    {
        using var recipients = await GetJson($"/templates/{plan.TemplateId}/recipients", ct);
        foreach (var property in recipients.RootElement.EnumerateObject())
            if (property.Name != "signers" && property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0)
                throw new InvalidOperationException("La plantilla contiene destinatarios adicionales.");
        var signers = recipients.RootElement.GetProperty("signers").EnumerateArray().ToArray();
        if (signers.Length != plan.Firmantes.Count) throw new InvalidOperationException("Cantidad de firmantes incorrecta en la plantilla.");
        var result = new List<DocuSignFirmante>();
        var labels = new HashSet<string>();
        var expectedLabels = TemplateLabels(plan.Firmantes.Count > 1);
        foreach (var contact in plan.Firmantes)
        {
            var role = Role(contact.Funcion);
            var matches = signers.Where(s => String(s, "roleName") == role).ToArray();
            if (matches.Length != 1 || String(matches[0], "clientUserId").Length > 0)
                throw new InvalidOperationException("Funciones de plantilla incorrectas o firma incrustada.");
            var id = String(matches[0], "recipientId");
            using var tabs = await GetJson($"/templates/{plan.TemplateId}/recipients/{Uri.EscapeDataString(id)}/tabs", ct);
            // Las acciones nativas son alternativas; no se rellenan ni se fuerzan ambas.
            var decisionRole = contact.Funcion == "Cliente" ? "" : contact.Funcion;
            RequireDecisionTab(tabs.RootElement, "approveTabs", $"Aprobar{decisionRole}", id);
            RequireDecisionTab(tabs.RootElement, "declineTabs", $"Declinar{decisionRole}", id);
            if (tabs.RootElement.TryGetProperty("textTabs", out var text))
                foreach (var tab in text.EnumerateArray())
                {
                    var label = String(tab, "tabLabel");
                    if (!expectedLabels.Contains(label))
                        throw new InvalidOperationException($"Etiqueta de texto inesperada en plantilla: {JsonSerializer.Serialize(label.Length > 100 ? label[..100] + "..." : label)} (funcion {contact.Funcion}). Revise la etiqueta de datos del campo en DocuSign.");
                    labels.Add(label);
                }
            result.Add(new(id, contact.Funcion, contact.Nombre, contact.Celular, "created"));
        }
        var missing = expectedLabels.Where(l => !labels.Contains(l)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Faltan etiquetas de datos en la plantilla: {string.Join(", ", missing)}.");
        return result;
    }

    private string Role(string function) => function switch
    {
        "Cliente" => options.IndividualRole,
        "Titular1" => options.JointFirstRole,
        "Titular2" => options.JointSecondRole,
        "Titular3" => options.JointThirdRole,
        _ => throw new InvalidOperationException("Funcion de firmante desconocida.")
    };

    public async Task<IReadOnlyList<DocuSignFirmante>> DraftSigners(Guid envelope,
        IReadOnlyList<DocuSignFirmante> templateSigners, CancellationToken ct)
    {
        // Composite templates puede renumerar recipientId al crear el sobre.
        // Vincular por roleName y guardar los IDs del sobre antes de enviarlo.
        using var recipients = await GetJson($"/envelopes/{envelope}/recipients", ct);
        foreach (var property in recipients.RootElement.EnumerateObject())
            if (property.Name != "signers" && property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0)
                throw new InvalidOperationException("El borrador contiene destinatarios adicionales.");
        var actual = recipients.RootElement.GetProperty("signers").EnumerateArray().ToArray();
        if (actual.Length != templateSigners.Count || templateSigners.Count is not (1 or 2 or 3))
            throw new InvalidOperationException("Cantidad de firmantes incorrecta en el borrador.");
        var result = new List<DocuSignFirmante>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var signer in templateSigners)
        {
            var role = Role(signer.Funcion);
            var matches = actual.Where(s => String(s, "roleName") == role).ToArray();
            if (matches.Length != 1 || String(matches[0], "clientUserId").Length > 0)
                throw new InvalidOperationException("Funciones del borrador incorrectas o firma incrustada.");
            var id = String(matches[0], "recipientId");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !ids.Add(id))
                throw new InvalidOperationException("Identificadores de firmantes invalidos en el borrador.");
            result.Add(signer with { RecipientId = id });
        }
        return result;
    }

    private static void RequireDecisionTab(JsonElement tabs, string collection, string label, string recipientId)
    {
        if (!tabs.TryGetProperty(collection, out var actions) || actions.ValueKind != JsonValueKind.Array
            || actions.GetArrayLength() != 1 || actions[0].ValueKind != JsonValueKind.Object
            || String(actions[0], "tabLabel") != label
            || (String(actions[0], "recipientId") is { Length: > 0 } assigned && assigned != recipientId)
            || String(actions[0], "conditionalParentLabel").Length > 0)
            throw new InvalidOperationException($"Cada titular debe tener el boton nativo {label}, sin condiciones y asignado a su destinatario.");
    }

    public async Task<Guid> CreateDraft(Propuesta propuesta, PropuestaClienteCreateRequest request,
        DocuSignPreparacion plan, IReadOnlyList<DocuSignFirmante> signers, CancellationToken ct)
    {
        static string Number(decimal? n) => n?.ToString("0.########", CultureInfo.InvariantCulture) ?? "No aplica";
        var values = new Dictionary<string, string>
        {
            ["TipoOperacion"] = propuesta.Tipo, ["Valor"] = propuesta.Instrumento,
            ["Cantidad"] = propuesta.Cantidad > 0 ? propuesta.Cantidad.ToString(CultureInfo.InvariantCulture) : "No aplica",
            ["Precio"] = propuesta.TipoOrden.Equals("Mercado", StringComparison.OrdinalIgnoreCase) ? "A mercado" : Number(propuesta.Precio),
            ["Moneda"] = request.Moneda, ["Monto"] = Number(propuesta.Monto), ["Vigencia"] = propuesta.Vigencia,
            ["Mercado"] = propuesta.Mercado, ["NombreRepresentante"] = propuesta.NombreOperador,
            [plan.Firmantes.Count > 1 ? "NombreCuenta" : "NombreCliente"] = plan.Firmantes[0].Nombre,
            [plan.Firmantes.Count > 1 ? "CodigoCuenta" : "CodigoCliente"] = propuesta.Cosabcli
        };
        var inline = new List<object>();
        foreach (var s in signers)
        {
            using var fields = await GetJson($"/templates/{plan.TemplateId}/recipients/{Uri.EscapeDataString(s.RecipientId)}/tabs", ct);
            var textTabs = new List<object>();
            if (fields.RootElement.TryGetProperty("textTabs", out var texts))
                foreach (var t in texts.EnumerateArray())
                {
                    var label = String(t, "tabLabel");
                    if (!values.TryGetValue(label, out var value)) throw new InvalidOperationException("La plantilla cambio durante la preparacion.");
                    textTabs.Add(new { tabLabel = label, value, locked = "true", required = "false" });
                }
            var role = Role(s.Funcion);
            // Los celulares de Opersab se normalizan como numeros de Peru (+51) antes de llegar aqui.
            inline.Add(new { roleName = role, recipientId = s.RecipientId, name = s.Nombre, email = "", routingOrder = "1",
                deliveryMethod = "WhatsApp", phoneNumber = new { countryCode = "51", number = s.Celular[3..] }, tabs = new { textTabs } });
        }
        using var response = await Request(HttpMethod.Post, "/envelopes", new
        {
            status = "created", emailSubject = $"Propuesta {propuesta.PropuestaId} - Autorizacion de orden",
            emailBlurb = "Revise la propuesta y seleccione «Aprobar» o «Declinar», según su decisión.",
            compositeTemplates = new[] { new { compositeTemplateId = "1", serverTemplates = new[] { new { sequence = "1", templateId = plan.TemplateId } },
                inlineTemplates = new[] { new { sequence = "2", recipients = new { signers = inline } } } } }
        }, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return Guid.Parse(String(json.RootElement, "envelopeId"));
    }

    public async Task Send(Guid envelope, CancellationToken ct)
    {
        using var response = await Request(HttpMethod.Put, $"/envelopes/{envelope}", new { status = "sent" }, ct);
    }

    public async Task<DocuSignEstado> Status(DocuSignSobre tracked, CancellationToken ct)
    {
        using var envelope = await GetJson($"/envelopes/{tracked.EnvelopeId}", ct);
        using var recipients = await GetJson($"/envelopes/{tracked.EnvelopeId}/recipients", ct);
        var root = envelope.RootElement;
        var state = String(root, "status");
        var actual = recipients.RootElement.GetProperty("signers").EnumerateArray().ToArray();
        if (actual.Length != tracked.FirmasRequeridas || tracked.Firmantes.Count != tracked.FirmasRequeridas
            || tracked.Firmantes.Any(s => actual.Count(a => String(a, "recipientId") == s.RecipientId) != 1))
            throw new InvalidOperationException("Los firmantes del sobre no coinciden con el registro.");
        var signers = tracked.Firmantes.Select(s =>
        {
            var signer = actual.Single(a => String(a, "recipientId") == s.RecipientId);
            return s with { Estado = String(signer, "status"), FechaFirmaUtc = Date(signer, "signedDateTime") };
        }).ToArray();
        if (state == "completed" && signers.Any(s => s.Estado != "completed"))
            throw new InvalidOperationException("El sobre no tiene todas las firmas completas.");
        return new(state, Date(root, "statusChangedDateTime") ?? DateTime.UtcNow, Date(root, "completedDateTime"), signers,
            Date(root, "sentDateTime"));
    }

    public async Task<byte[]> DownloadCombinedPdf(Guid envelopeId, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        ct = timeout.Token;
        const int maximumBytes = 50 * 1024 * 1024;
        using var response = await Request(HttpMethod.Get,
            $"/envelopes/{envelopeId}/documents/combined?certificate=true", null, ct, HttpCompletionOption.ResponseHeadersRead);
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new IOException("El documento supera 50 MB.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > maximumBytes) throw new IOException("El documento supera 50 MB.");
            output.Write(buffer, 0, count);
        }
        if (output.Length < 5 || !output.GetBuffer().AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw new IOException("DocuSign no devolvió un PDF.");
        return output.ToArray();
    }

    private static DateTime? Date(JsonElement e, string name) => DateTimeOffset.TryParse(String(e, name),
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date.UtcDateTime : null;
    private static string String(JsonElement e, string name) => e.TryGetProperty(name, out var value) ? value.ToString() : "";
    public void Dispose() => _auth.Dispose();
}
