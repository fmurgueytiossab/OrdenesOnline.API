using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrdenesOnline.Application.Services;

namespace OrdenesOnline_API.Features.Propuestas;

[ApiController]
[Authorize]
[Route("api/Propuesta")]
public sealed class PropuestaDocumentoController(DocuSignDocumentoService service,
    ILogger<PropuestaDocumentoController> logger) : ControllerBase
{
    [HttpGet("{propuestaId:int:min(1)}/documento")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Download(int propuestaId, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var representanteId)
            || representanteId <= 0)
            return Problem(title: "La identidad autenticada no contiene un usuario válido.", statusCode: 401);

        try
        {
            var result = await service.Download(representanteId, propuestaId, ct);
            return result.Status switch
            {
                DocuSignDocumentoStatus.RepresentanteNotFound => Problem(
                    title: "El usuario autenticado ya no existe.", statusCode: 401),
                DocuSignDocumentoStatus.NotFound => Problem(title: "Documento no encontrado.", statusCode: 404),
                DocuSignDocumentoStatus.NotCompleted => Problem(
                    title: "El sobre todavía no tiene todas las firmas completas.", statusCode: 409),
                _ => File(result.Pdf!, "application/pdf", $"Propuesta-{propuestaId}.pdf")
            };
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
            && ex is HttpRequestException or IOException or InvalidOperationException
                or OperationCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning("No se pudo obtener el PDF DocuSign: propuesta {PropuestaId}, tipo {ErrorType}.",
                propuestaId, ex.GetType().Name);
            return Problem(title: "No se pudo obtener el documento de DocuSign. Intente nuevamente.", statusCode: 502);
        }
    }
}
