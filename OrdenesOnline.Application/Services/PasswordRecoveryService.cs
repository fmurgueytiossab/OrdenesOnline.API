using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;

namespace OrdenesOnline.Application.Services;

public sealed class PasswordRecoveryService
{
    private readonly RepresentanteService _representanteService;
    private readonly ActionTokenService _actionTokenService;
    private readonly IEmailService _emailService;
    private readonly ILogger<PasswordRecoveryService> _logger;
    private readonly string _frontendUrl;

    public PasswordRecoveryService(
        RepresentanteService representanteService,
        ActionTokenService actionTokenService,
        IEmailService emailService,
        ILogger<PasswordRecoveryService> logger,
        IConfiguration configuration)
    {
        _representanteService = representanteService;
        _actionTokenService = actionTokenService;
        _emailService = emailService;
        _logger = logger;
        _frontendUrl = configuration["App:FrontendUrl"]?.TrimEnd('/')
            ?? throw new InvalidOperationException("Falta la configuración obligatoria 'App:FrontendUrl'.");

        if (string.IsNullOrWhiteSpace(_frontendUrl))
        {
            throw new InvalidOperationException("Falta la configuración obligatoria 'App:FrontendUrl'.");
        }
    }

    public async Task SendPasswordResetEmail(
        string email,
        CancellationToken cancellationToken = default)
    {
        var representante = await _representanteService.GetByEmail(email, cancellationToken);
        if (representante is null)
        {
            return;
        }

        var token = await _actionTokenService.CreatePasswordResetTokenAsync(
            representante.RepresentanteId,
            cancellationToken);
        var link = $"{_frontendUrl}/change-password?token={Uri.EscapeDataString(token.Value)}";
        var safeLink = WebUtility.HtmlEncode(link);
        var html = $"""
            <p>Para obtener una nueva contraseña, abra el siguiente enlace:</p>
            <a href="{safeLink}" style="color: #1a73e8; font-size: 16px;">Nueva contraseña</a>
            """;

        try
        {
            await _emailService.SendEmailAsync(
                email,
                "Recuperar contraseña de Órdenes Online",
                html,
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                exception,
                "No se pudo enviar el correo de recuperación para el representante {RepresentanteId}.",
                representante.RepresentanteId);
        }
    }

    public async Task<string?> ValidatePasswordResetToken(
        string token,
        CancellationToken cancellationToken = default)
    {
        var storedToken = await _actionTokenService.ValidateAsync(
            token,
            ActionTokenService.PasswordResetType,
            cancellationToken);

        if (storedToken is null)
        {
            return null;
        }

        var representante = await _representanteService.GetById(
            storedToken.UserId,
            cancellationToken);

        return representante?.CorreoCorporativo;
    }
}
