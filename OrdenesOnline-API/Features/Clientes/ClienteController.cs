using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.DTO;
using System.IdentityModel.Tokens.Jwt;

namespace OrdenesOnline_API.Features.Clientes;

[ApiController]
[Authorize]
[Route("api/Cliente")]
public sealed class ClienteController : ControllerBase
{
    private readonly ClienteService _service;

    public ClienteController(ClienteService service)
    {
        _service = service;
    }

    [HttpGet("buscar")]
    [ProducesResponseType<IReadOnlyList<ClienteSearchResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ClienteSearchResult>>> Search(
        [FromQuery(Name = "q")] string? search,
        [FromQuery] int limit = ClienteService.DefaultResultLimit,
        CancellationToken cancellationToken = default)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!int.TryParse(subject, out var representanteId))
        {
            return Problem(
                title: "La identidad autenticada no contiene un usuario válido.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await _service.Search(
            representanteId,
            search,
            limit,
            cancellationToken);

        return result.Status switch
        {
            ClienteSearchStatus.InvalidSearch => Problem(
                title: "El texto de búsqueda no es válido.",
                detail: $"Ingrese entre {ClienteService.MinimumSearchLength} y {ClienteService.MaximumSearchLength} caracteres.",
                statusCode: StatusCodes.Status400BadRequest),
            ClienteSearchStatus.RepresentanteNotFound => Problem(
                title: "El usuario autenticado ya no existe.",
                statusCode: StatusCodes.Status401Unauthorized),
            _ => Ok(result.Items ?? [])
        };
    }
}
