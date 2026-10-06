using System.Net;
using System.Text;
using System.Text.Json;

var originalIn = Console.In;
var originalOut = Console.Out;
foreach (var scenario in new[] { "individual", "mancomunada", "missing-field", "optional-signature", "cancel", "timeout", "pending" })
{
    using var handler = new FakeDocusign(scenario);
    using var client = new HttpClient(handler);
    using var output = new StringWriter();
    try
    {
        Console.SetOut(output);
        var selection = scenario == "mancomunada" ? "2" : "1";
        var confirmation = scenario == "cancel" ? "NO" : "ENVIAR";
        Console.SetIn(new StringReader($"{selection}\nPersona prueba\n51\n999999991\n"
            + (selection == "2" ? "Segunda persona\n51\n999999992\n" : "") + confirmation + "\n"));
        var result = scenario == "pending"
            ? await DocusignTemplateTest.ShowStatus(client, "https://example.invalid", "test")
            : await DocusignTemplateTest.Send(client, "https://example.invalid");
        var shouldSend = scenario is "individual" or "mancomunada" or "timeout";
        if (handler.Posts != (shouldSend ? 1 : 0)) throw new Exception("Numero de envios inesperado.");
        if ((scenario is "missing-field" or "optional-signature" or "timeout") != (result == 1))
            throw new Exception("Resultado inesperado.");
        if (scenario == "pending" && !output.ToString().Contains("No se debe aceptar"))
            throw new Exception("Una firma pendiente no debe aceptar la propuesta.");
    }
    finally { Console.SetIn(originalIn); Console.SetOut(originalOut); }
    Console.WriteLine($"OK: {scenario}");
}

foreach (var scenario in new[] { "archive-completed", "archive-pending", "archive-declined", "archive-invalid-pdf" })
{
    var testDirectory = Path.Combine(Path.GetTempPath(), "DocusignArchiveCheck-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(testDirectory);
    using var handler = new FakeDocusign(scenario);
    using var client = new HttpClient(handler);
    using var output = new StringWriter();
    const string envelopeId = "00000000-0000-0000-0000-000000000001";
    try
    {
        Console.SetOut(output);
        var result = await DocusignSignedDocument.Run(client, "https://example.invalid", envelopeId, 123,
            false, () => Task.FromResult(true), testDirectory);
        var expected = scenario == "archive-completed";
        if ((result == 0) != expected || File.Exists(Path.Combine(testDirectory, "123.pdf")) != expected)
            throw new Exception("Archivado inesperado.");
        if (handler.Downloads != (scenario is "archive-completed" or "archive-invalid-pdf" ? 1 : 0))
            throw new Exception("Se descargo un sobre pendiente/rechazado.");
        if (expected)
        {
            var repeat = await DocusignSignedDocument.Run(client, "https://example.invalid", envelopeId, 123,
                false, () => Task.FromResult(true), testDirectory);
            if (repeat != 0 || handler.Downloads != 1) throw new Exception("La repeticion no fue idempotente.");
            var conflict = await DocusignSignedDocument.Run(client, "https://example.invalid",
                "00000000-0000-0000-0000-000000000002", 123, false, () => Task.FromResult(true), testDirectory);
            if (conflict != 1 || handler.Downloads != 1) throw new Exception("Se permitio cambiar el sobre del PDF.");
            File.Delete(Path.Combine(testDirectory, "123.pdf"));
            var recovered = await DocusignSignedDocument.Run(client, "https://example.invalid", envelopeId, 123,
                false, () => Task.FromResult(true), testDirectory);
            if (recovered != 0 || handler.Downloads != 2) throw new Exception("No recupero una descarga interrumpida.");
        }
    }
    finally
    {
        Console.SetOut(originalOut);
        Directory.Delete(testDirectory, recursive: true);
    }
    Console.WriteLine($"OK: {scenario}");
}

sealed class FakeDocusign(string scenario) : HttpMessageHandler
{
    public int Posts { get; private set; }
    public int Downloads { get; private set; }
    private static readonly string[] Labels = ["Operacion", "Valor", "Cantidad", "TipoOrden", "Precio", "Moneda", "Monto", "Vigencia", "Mercado", "NombreRepresentante", "CodigoCliente"];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/documents/combined"))
        {
            Downloads++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes(scenario == "archive-invalid-pdf"
                    ? "pagina de error" : "%PDF-1.7\nPDF simulado para comprobar archivado\n%%EOF"))
            };
        }
        if (request.Method == HttpMethod.Post)
        {
            Posts++;
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = payload.RootElement;
            var composite = root.GetProperty("compositeTemplates")[0];
            var signers = composite.GetProperty("inlineTemplates")[0].GetProperty("recipients").GetProperty("signers");
            var expectedCount = scenario == "mancomunada" ? 2 : 1;
            if (signers.GetArrayLength() != expectedCount || root.GetProperty("status").GetString() != "sent")
                throw new Exception("Firmantes o estado incorrectos.");
            var expectedTemplate = scenario == "mancomunada" ? "10c7cb91-971e-4d02-aef9-54c5dea3a270" : "a412ccfb-74da-4a7d-b831-a3c354f10c41";
            if (composite.GetProperty("serverTemplates")[0].GetProperty("templateId").GetString() != expectedTemplate)
                throw new Exception("Plantilla incorrecta.");
            foreach (var signer in signers.EnumerateArray())
            {
                if (signer.GetProperty("deliveryMethod").GetString() != "WhatsApp" || signer.GetProperty("email").GetString() != "")
                    throw new Exception("Canal incorrecto.");
                foreach (var tab in signer.GetProperty("tabs").GetProperty("textTabs").EnumerateArray())
                    if (tab.GetProperty("locked").GetString() != "true") throw new Exception("Datos editables.");
            }
            if (scenario == "timeout") throw new TaskCanceledException();
            return Json(new { envelopeId = "sobre-prueba", status = "sent" });
        }
        if (path.EndsWith("/tabs"))
            return Json(new
            {
                signHereTabs = new[] { new { optional = scenario == "optional-signature" ? "true" : "false" } },
                textTabs = Labels.Where(l => scenario != "missing-field" || l != "Moneda").Select(l => new { tabLabel = l })
            });
        if (path.EndsWith("/recipients"))
            return Json(new
            {
                signers = (scenario is "mancomunada" or "pending" ? new[] { "Titular1", "Titular2" } : new[] { "Cliente" })
                    .Select((role, i) => new { roleName = role, recipientId = (i + 1).ToString(), status = i == 0 ? "completed" : "sent" })
            });
        return Json(new { status = scenario switch
        {
            "archive-completed" or "archive-invalid-pdf" => "completed",
            "archive-declined" => "declined",
            _ => "sent"
        }, completedDateTime = "2026-10-05T12:00:00Z" });
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}
