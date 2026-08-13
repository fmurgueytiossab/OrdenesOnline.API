using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.DTO;
using System.IdentityModel.Tokens.Jwt;

namespace OrdenesOnline_API.Features.Authentication;

[ApiController]
[Route("api/Representante")]
public sealed class RepresentanteController : ControllerBase
{
    private readonly RepresentanteService _service;
    private readonly TokenService _tokenService;

    public RepresentanteController(RepresentanteService service, TokenService tokenService)
    {
        _service = service;
        _tokenService = tokenService;
    }

    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Correo.Trim();
        var result = await _service.Login(email, request.Password, cancellationToken);

        if (result is null || result.IsValid == 0)
        {
            return Problem(
                title: "Credenciales inválidas.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var token = _tokenService.GenerateAccessToken(email, result.RepresentanteId);
        return Ok(new LoginResponse(true, result.RepresentanteId, token));
    }

    [AllowAnonymous]
    [EnableRateLimiting("password-recovery")]
    [HttpPost("update-password")]
    [ProducesResponseType<PasswordUpdateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PasswordUpdateResponse>> UpdatePassword(
        [FromBody] UpdatePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _service.UpdatePasswordByToken(
            request.Token,
            request.Password,
            cancellationToken);

        if (!updated)
        {
            return Problem(
                title: "El token es inválido, expiró o ya no corresponde al usuario.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok(new PasswordUpdateResponse(true, "Contraseña cambiada correctamente."));
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<RepresentanteDTO>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RepresentanteDTO>> Me(CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!int.TryParse(subject, out var representanteId))
        {
            return Problem(
                title: "La identidad autenticada no contiene un usuario válido.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var representante = await _service.GetById(representanteId, cancellationToken);
        if (representante is null)
        {
            return Problem(
                title: "El usuario autenticado ya no existe.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Ok(representante);
    }
}

public sealed record LoginResponse(bool IsValid, int UserId, string Token);
public sealed record PasswordUpdateResponse(bool IsValid, string Mensaje);
