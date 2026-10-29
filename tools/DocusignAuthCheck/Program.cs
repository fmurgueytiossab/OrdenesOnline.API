using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Autentica en demo. El envio de prueba requiere --send-test y confirmacion local.
const string authHost = "account-d.docusign.com";
if (args.Contains("--help"))
{
    Console.WriteLine("Prueba interactiva de JWT DocuSign en desarrollo.");
    Console.WriteLine("Solicita Integration Key, User ID, Account ID y ruta del PEM RSA.");
    Console.WriteLine("No guarda credenciales ni muestra tokens. Sin opciones, solo verifica autenticacion.");
    Console.WriteLine("--send-test: completa una plantilla y envia por WhatsApp tras confirmacion.");
    Console.WriteLine("--status ID: consulta el estado de un sobre sin volver a enviarlo.");
    return 0;
}
if (args.Length != 0 && !args.SequenceEqual(new[] { "--send-test" })
    && !(args.Length == 2 && args[0] == "--status" && Guid.TryParse(args[1], out _)))
{
    Console.WriteLine("Opciones validas: --help, --send-test, --status ID.");
    return 1;
}

var stage = "lectura de identificadores";
try
{
    var clientId = ReadGuid("Clave de integracion (Integration Key)");
    var userId = ReadGuid("Identificacion de usuario (User ID que acepto el consentimiento)");
    var accountId = ReadGuid("ID de la cuenta API (Account ID)");
    stage = "lectura del archivo RSA";
    Console.Write("Ruta completa del archivo de clave privada RSA (.pem o .key): ");
    var keyPath = (Console.ReadLine() ?? "").Trim().Trim('"');
    if (!File.Exists(keyPath))
    {
        Console.WriteLine("No se encontro el archivo RSA. Revisa la ruta.");
        return 1;
    }

    var pem = await File.ReadAllTextAsync(keyPath);
    if (pem.Contains("-----BEGIN ENCRYPTED PRIVATE KEY-----", StringComparison.Ordinal))
    {
        Console.WriteLine("El archivo contiene una clave cifrada. Esta utilidad necesita un PEM privado sin cifrar, guardado de forma segura.");
        return 1;
    }
    if (!pem.Contains("-----BEGIN RSA PRIVATE KEY-----", StringComparison.Ordinal)
        && !pem.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal))
    {
        Console.WriteLine("El archivo no contiene una clave PRIVADA PEM reconocible.");
        Console.WriteLine("Debe incluir BEGIN RSA PRIVATE KEY y END RSA PRIVATE KEY, o BEGIN PRIVATE KEY y END PRIVATE KEY.");
        Console.WriteLine("Copia la clave privada completa; no la clave publica ni el ID del par RSA.");
        return 1;
    }
    stage = "importacion de la clave privada RSA";
    using var rsa = RSA.Create();
    rsa.ImportFromPem(pem);
    stage = "firma local del JWT";
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
    var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
    {
        iss = clientId, sub = userId, aud = authHost,
        iat = now, exp = now + 3600, scope = "signature impersonation"
    }));
    var unsignedJwt = $"{header}.{payload}";
    var signature = rsa.SignData(Encoding.UTF8.GetBytes(unsignedJwt),
        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    using var body = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
        ["assertion"] = $"{unsignedJwt}.{Base64Url(signature)}"
    });
    stage = "solicitud del token a DocuSign";
    using var response = await client.PostAsync($"https://{authHost}/oauth/token", body);
    Console.WriteLine($"Respuesta del servidor de autenticacion: HTTP {(int)response.StatusCode}.");
    stage = "lectura de la respuesta del token";
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    if (!response.IsSuccessStatusCode)
    {
        var error = json.RootElement.TryGetProperty("error", out var value) ? value.GetString() : null;
        Console.WriteLine($"Autenticacion rechazada (HTTP {(int)response.StatusCode}).");
        Console.WriteLine(error switch
        {
            "consent_required" => "consent_required: falta consentimiento para esta aplicacion y usuario.",
            "invalid_grant" => "invalid_grant: revisa usuario, par RSA, entorno y reloj del equipo.",
            "invalid_client" => "invalid_client: revisa la clave de integracion y su entorno.",
            _ => "DocuSign devolvio un error de autenticacion; no se muestra el cuerpo para proteger credenciales."
        });
        return 1;
    }

    var token = json.RootElement.GetProperty("access_token").GetString();
    if (string.IsNullOrWhiteSpace(token))
        throw new InvalidOperationException();
    Console.WriteLine("JWT OK: DocuSign emitio un token. Consentimiento confirmado.");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    stage = "consulta de acceso a la cuenta";
    using var infoResponse = await client.GetAsync($"https://{authHost}/oauth/userinfo");
    if (!infoResponse.IsSuccessStatusCode)
    {
        Console.WriteLine($"No se pudo verificar la cuenta (HTTP {(int)infoResponse.StatusCode}).");
        return 1;
    }
    stage = "lectura de la respuesta de cuentas";
    using var info = JsonDocument.Parse(await infoResponse.Content.ReadAsStringAsync());
    foreach (var account in info.RootElement.GetProperty("accounts").EnumerateArray())
    {
        if (string.Equals(account.GetProperty("account_id").GetString(), accountId,
            StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("CUENTA OK: el usuario tiene acceso al Account ID indicado.");
            var profileName = info.RootElement.TryGetProperty("name", out var nameValue)
                && nameValue.ValueKind == JsonValueKind.String ? nameValue.GetString() : null;
            var accountName = account.TryGetProperty("account_name", out var accountValue)
                && accountValue.ValueKind == JsonValueKind.String ? accountValue.GetString() : null;
            Console.WriteLine($"Nombre del usuario en OAuth/userinfo: {JsonSerializer.Serialize(profileName)}");
            Console.WriteLine($"Nombre de la cuenta en OAuth/userinfo: {JsonSerializer.Serialize(accountName)}");
            if (args.Length != 0)
            {
                stage = "prueba de plantilla y envio DocuSign";
                var baseUrl = account.GetProperty("base_uri").GetString();
                if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var apiBase)
                    || apiBase.Scheme != "https" || apiBase.Host != "demo.docusign.net")
                {
                    Console.WriteLine("La cuenta no apunta al servidor demo esperado. La prueba se detuvo.");
                    return 1;
                }
                var apiRoot = $"{apiBase.GetLeftPart(UriPartial.Authority)}/restapi/v2.1/accounts/{accountId}";
                return args[0] == "--status" ? await DocusignTemplateTest.ShowStatus(client, apiRoot, args[1])
                    : await DocusignTemplateTest.Send(client, apiRoot);
            }
            Console.WriteLine("Esta prueba no verifica permisos de WhatsApp ni envia documentos.");
            return 0;
        }
    }
    Console.WriteLine("El token es valido, pero el usuario no tiene acceso al Account ID indicado.");
    return 1;
}
catch (CryptographicException)
{
    Console.WriteLine($"Error en {stage} (CryptographicException).");
    Console.WriteLine("No se pudo usar la clave RSA. Revisa que sea la clave privada PEM completa y conserve sus saltos de linea.");
    return 1;
}
catch (ArgumentException)
{
    Console.WriteLine($"Error en {stage} (ArgumentException).");
    Console.WriteLine("Revisa el formato PEM: una unica clave privada completa, con encabezado, cierre y saltos de linea reales.");
    return 1;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
{
    Console.WriteLine($"Error en {stage} ({exception.GetType().Name}).");
    Console.WriteLine("No se pudo leer el archivo. Revisa su disponibilidad y los permisos de lectura.");
    return 1;
}
catch (JsonException)
{
    Console.WriteLine($"Error en {stage} (JsonException).");
    Console.WriteLine("El servidor no devolvio JSON valido. Revisa si un proxy o una pagina de error esta interceptando la respuesta.");
    return 1;
}
catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
{
    Console.WriteLine("No se pudo completar la conexion HTTPS con DocuSign. Revisa red y proxy.");
    return 1;
}
catch (Exception exception)
{
    Console.WriteLine($"No se pudo completar la etapa: {stage} ({exception.GetType().Name}).");
    Console.WriteLine("No se muestran el contenido de la clave ni la respuesta completa para proteger credenciales.");
    return 1;
}

static string ReadGuid(string label)
{
    while (true)
    {
        Console.Write($"{label}: ");
        var input = Console.ReadLine();
        if (input is null)
            throw new EndOfStreamException();
        if (Guid.TryParse(input.Trim(), out var value))
            return value.ToString();
        Console.WriteLine($"El campo '{label}' debe ser un identificador UUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx). Vuelve a ingresarlo.");
    }
}

static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
