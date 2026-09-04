using System.IdentityModel.Tokens.Jwt;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.DTO;

namespace OrdenesOnline_API.Features.Propuestas;

[ApiController]
[Authorize]
[Route("api/PropuestaCliente")]
public sealed class PropuestaClienteController : ControllerBase
{
    private readonly PropuestaClienteService _service;
    private readonly PropuestaBvlSeguimientoService _seguimientoService;

    public PropuestaClienteController(
        PropuestaClienteService service,
        PropuestaBvlSeguimientoService seguimientoService)
    {
        _service = service;
        _seguimientoService = seguimientoService;
    }

    [HttpGet("seguimiento/bvl")]
    [ProducesResponseType<PropuestaBvlSeguimientoPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PropuestaBvlSeguimientoPage>> GetBvlTracking(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PropuestaBvlSeguimientoService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!int.TryParse(subject, out var representanteId))
        {
            return Problem(
                title: "La identidad autenticada no contiene un usuario válido.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await _seguimientoService.Get(
            representanteId,
            page,
            pageSize,
            cancellationToken);

        return result.Status switch
        {
            PropuestaBvlSeguimientoStatus.InvalidPagination => Problem(
                title: "La paginación indicada no es válida.",
                detail: $"La página debe ser mayor que cero y pageSize debe estar entre 1 y {PropuestaBvlSeguimientoService.MaximumPageSize}.",
                statusCode: StatusCodes.Status400BadRequest),
            PropuestaBvlSeguimientoStatus.RepresentanteNotFound => Problem(
                title: "El usuario autenticado ya no existe.",
                statusCode: StatusCodes.Status401Unauthorized),
            _ => Ok(result.Page)
        };
    }

    [HttpPost]
    [ProducesResponseType<CreatePropuestaClienteResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CreatePropuestaClienteResponse>> Post(
        [FromBody] PropuestaClienteCreateRequest request,
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
            CreatePropuestaClienteStatus.RepresentanteNotFound => Problem(
                title: "El usuario autenticado ya no existe.",
                statusCode: StatusCodes.Status401Unauthorized),
            CreatePropuestaClienteStatus.CosabcliForbidden => Problem(
                title: "El usuario no tiene acceso al código de cliente indicado.",
                statusCode: StatusCodes.Status403Forbidden),
            CreatePropuestaClienteStatus.InvalidMarket => Problem(
                title: "El mercado indicado no es válido.",
                detail: "Los valores permitidos son BVL, Canaccord Renta4 y Pershing.",
                statusCode: StatusCodes.Status400BadRequest),
            _ => StatusCode(
                StatusCodes.Status201Created,
                new CreatePropuestaClienteResponse(
                    true,
                    result.PropuestaId!.Value,
                    result.EmailDelivered,
                    result.EmailDelivered
                        ? "Propuesta creada y resumen enviado al cliente."
                        : "La propuesta fue guardada correctamente.",
                    result.EmailDelivered
                        ? null
                        : "No se pudo enviar el correo de resumen al cliente."))
        };
    }

    [AllowAnonymous]
    [EnableRateLimiting("proposal-review")]
    [HttpPost("revision/validar")]
    [ProducesResponseType<PropuestaClienteReviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PropuestaClienteReviewResponse>> Review(
        [FromBody] PropuestaClienteReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetReview(request.Token, cancellationToken);
        if (result.Status != PropuestaClienteReviewStatus.Valid || result.Propuesta is null)
        {
            return Problem(
                title: "El token es inválido, expiró o ya fue utilizado.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var propuesta = result.Propuesta;
        return Ok(new PropuestaClienteReviewResponse(
            propuesta.PropuestaId,
            propuesta.Tipo,
            propuesta.Cantidad,
            propuesta.Instrumento,
            propuesta.TipoOrden,
            propuesta.Precio,
            propuesta.Monto,
            propuesta.Mercado,
            propuesta.Vigencia,
            propuesta.Estado));
    }

    [AllowAnonymous]
    [EnableRateLimiting("proposal-review")]
    [HttpPost("revision")]
    [ProducesResponseType<PropuestaClienteDecisionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PropuestaClienteDecisionResponse>> Decide(
        [FromBody] PropuestaClienteDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.Decide(request.Token, request.Estado, cancellationToken);

        return result.Status switch
        {
            PropuestaClienteDecisionStatus.InvalidToken => Problem(
                title: "El token es inválido, expiró o ya fue utilizado.",
                statusCode: StatusCodes.Status400BadRequest),
            PropuestaClienteDecisionStatus.InvalidDecision => Problem(
                title: "El estado indicado no es válido.",
                detail: "Los valores permitidos son Aceptado y Cancelado.",
                statusCode: StatusCodes.Status400BadRequest),
            PropuestaClienteDecisionStatus.AlreadyDecided => Problem(
                title: "La propuesta ya fue respondida.",
                detail: $"El estado actual es {result.Estado}.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Ok(new PropuestaClienteDecisionResponse(
                true,
                result.PropuestaId!.Value,
                result.Estado!,
                "La respuesta de la propuesta fue registrada correctamente."))
        };
    }
}

public sealed record CreatePropuestaClienteResponse(
    bool Success,
    int PropuestaId,
    bool EmailDelivered,
    string Message,
    string? Warning);

public sealed record PropuestaClienteReviewResponse(
    int PropuestaId,
    string Tipo,
    int Cantidad,
    string Instrumento,
    string TipoOrden,
    decimal? Precio,
    decimal? Monto,
    string Mercado,
    string Vigencia,
    string Estado);

public sealed class PropuestaClienteReviewRequest
{
    [Required, StringLength(256)]
    public string Token { get; set; } = string.Empty;
}

public sealed class PropuestaClienteDecisionRequest
{
    [Required, StringLength(256)]
    public string Token { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string Estado { get; set; } = string.Empty;
}

public sealed record PropuestaClienteDecisionResponse(
    bool Success,
    int PropuestaId,
    string Estado,
    string Message);
