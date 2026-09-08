using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.DTO;
using System.IdentityModel.Tokens.Jwt;

namespace OrdenesOnline_API.Features.Propuestas;

[ApiController]
[Authorize]
[Route("api/Propuesta")]
public sealed class PropuestaController : ControllerBase
{
    private readonly PropuestaService _service;

    public PropuestaController(PropuestaService service)
    {
        _service = service;
    }

    [HttpPost]
    [ProducesResponseType<CreatePropuestaResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatePropuestaResponse>> Post(
        [FromBody] PropuestaCreateRequest request,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!int.TryParse(subject, out var representanteId))
        {
            return Problem(
                title: "La identidad autenticada no contiene un usuario válido.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await _service.Create(representanteId, request, cancellationToken);

        return result.Status switch
        {
            CreatePropuestaStatus.MarketClosed or CreatePropuestaStatus.InvalidValidity => Problem(
                title: result.Message, statusCode: StatusCodes.Status409Conflict),
            CreatePropuestaStatus.RepresentanteNotFound => Problem(
                title: "El usuario autenticado ya no existe.",
                statusCode: StatusCodes.Status401Unauthorized),
            _ => StatusCode(
                StatusCodes.Status201Created,
                new CreatePropuestaResponse(
                    true,
                    result.PropuestaId!.Value,
                    result.NotificationDelivered,
                    result.NotificationDelivered
                        ? "Propuesta creada correctamente."
                        : "La propuesta fue guardada correctamente.",
                    result.NotificationDelivered
                        ? null
                        : "La propuesta fue guardada, pero no se pudo enviar a Zapier."))
        };
    }
}

public sealed record CreatePropuestaResponse(
    bool Success,
    int PropuestaId,
    bool NotificationDelivered,
    string Message,
    string? Warning);
