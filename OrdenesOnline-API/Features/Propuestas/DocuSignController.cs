using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline_API.Features.Propuestas;

[ApiController]
[Route("api/DocuSign")]
public sealed class DocuSignController(DocuSignOptions options, DocuSignClient client,
    IPropuestaDocuSignRepository repository, ILogger<DocuSignController> logger) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("webhook")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        // Autenticar los bytes originales, antes de deserializar o formatear JSON.
        using var body = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await Request.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (body.Length + count > 1024 * 1024) return StatusCode(413);
            body.Write(buffer, 0, count);
        }
        var signatures = Request.Headers.Where(h => h.Key.StartsWith("X-Docusign-Signature-", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value).Where(v => v is not null).Select(v => v!);
        if (!DocuSignHmac.Validate(body.ToArray(), options.WebhookHmacSecret, signatures)) return Unauthorized();
        Guid envelopeId;
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var root = document.RootElement;
            if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("accountId", out var account)
                || !Guid.TryParse(account.GetString(), out var accountId)
                || !data.TryGetProperty("envelopeId", out var envelope) || !Guid.TryParse(envelope.GetString(), out envelopeId))
                return BadRequest();
            // La configuracion puede publicar otros sobres de la cuenta. Solo procesar los registrados aqui.
            if (accountId != options.AccountId) return Ok();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return BadRequest(); }
        try
        {
            var tracked = await repository.FindAsync(envelopeId, options.AccountId, ct);
            if (tracked is null) return Ok();
            // Consultar el estado actual evita aceptar mensajes atrasados, duplicados o la firma de solo un titular.
            var state = await client.Status(tracked, ct);
            await repository.ApplyAsync(tracked, state, ct);
            // El worker descarga desde estados guardados: una interrupcion no pierde el trabajo.
            return Ok();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Webhook DocuSign pendiente: sobre {EnvelopeId}, tipo {ErrorType}.", envelopeId, ex.GetType().Name);
            return StatusCode(503); // Connect reintenta; no confirmar antes de persistir.
        }
    }
}
