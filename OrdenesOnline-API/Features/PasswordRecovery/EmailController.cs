using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.DTO;

namespace OrdenesOnline_API.Features.PasswordRecovery;

[ApiController]
[Route("api/Email")]
public sealed class EmailController : ControllerBase
{
    private const string GenericMessage =
        "Si el correo existe, se enviaron instrucciones para restablecer la contraseña.";

    private readonly PasswordRecoveryService _passwordRecoveryService;

    public EmailController(PasswordRecoveryService passwordRecoveryService)
    {
        _passwordRecoveryService = passwordRecoveryService;
    }

    [AllowAnonymous]
    [EnableRateLimiting("password-recovery")]
    [HttpPost("send-validation")]
    [ProducesResponseType<PasswordRecoveryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PasswordRecoveryResponse>> SendValidationEmail(
        [FromBody] PasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        await _passwordRecoveryService.SendPasswordResetEmail(
            request.Email.Trim(),
            cancellationToken);

        return Ok(new PasswordRecoveryResponse(GenericMessage));
    }

    [AllowAnonymous]
    [EnableRateLimiting("password-recovery")]
    [HttpGet("validate")]
    [ProducesResponseType<PasswordResetValidationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<PasswordResetValidationResponse> ValidateEmail([FromQuery] string token)
    {
        var email = _passwordRecoveryService.ValidatePasswordResetToken(token);
        if (email is null)
        {
            return Problem(
                title: "Token inválido o expirado.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok(new PasswordResetValidationResponse(email));
    }
}

public sealed record PasswordRecoveryResponse(string Mensaje);
public sealed record PasswordResetValidationResponse(string Email);
