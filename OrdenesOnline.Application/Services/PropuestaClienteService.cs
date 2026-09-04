using System.Globalization;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

public sealed class PropuestaClienteService
{
    private readonly IPropuestaRepository _propuestaRepository;
    private readonly IRepresentanteRepository _representanteRepository;
    private readonly IRepresentanteClientScopeRepository _clientScopeRepository;
    private readonly IEmailService _emailService;
    private readonly ActionTokenService _actionTokenService;
    private readonly ILogger<PropuestaClienteService> _logger;
    private readonly string _clientesFrontendUrl;

    public PropuestaClienteService(
        IPropuestaRepository propuestaRepository,
        IRepresentanteRepository representanteRepository,
        IRepresentanteClientScopeRepository clientScopeRepository,
        IEmailService emailService,
        ActionTokenService actionTokenService,
        ILogger<PropuestaClienteService> logger,
        IConfiguration configuration)
    {
        _propuestaRepository = propuestaRepository;
        _representanteRepository = representanteRepository;
        _clientScopeRepository = clientScopeRepository;
        _emailService = emailService;
        _actionTokenService = actionTokenService;
        _logger = logger;
        _clientesFrontendUrl = configuration["App:ClientesFrontendUrl"]?.TrimEnd('/')
            ?? throw new InvalidOperationException(
                "Falta la configuración obligatoria 'App:ClientesFrontendUrl'.");

        if (string.IsNullOrWhiteSpace(_clientesFrontendUrl))
        {
            throw new InvalidOperationException(
                "Falta la configuración obligatoria 'App:ClientesFrontendUrl'.");
        }
    }

    public async Task<CreatePropuestaClienteResult> Create(
        int representanteId,
        PropuestaClienteCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!PropuestaClienteCreateRequest.TryGetCanonicalMarket(request.Mercado, out var mercado))
        {
            return new CreatePropuestaClienteResult(CreatePropuestaClienteStatus.InvalidMarket);
        }

        var representante = await _representanteRepository.GetByIdAsync(
            representanteId,
            cancellationToken);

        if (representante is null)
        {
            return new CreatePropuestaClienteResult(CreatePropuestaClienteStatus.RepresentanteNotFound);
        }

        var clientScope = await _clientScopeRepository.GetAsync(
            representanteId,
            cancellationToken);
        if (!clientScope.RepresentanteExiste)
        {
            return new CreatePropuestaClienteResult(CreatePropuestaClienteStatus.RepresentanteNotFound);
        }

        if (!clientScope.Cosabcli.Contains(request.Cosabcli.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return new CreatePropuestaClienteResult(CreatePropuestaClienteStatus.CosabcliForbidden);
        }

        var propuesta = new Propuesta
        {
            NombreOperador = representante.Nombre,
            CorreoCorporativo = representante.CorreoCorporativo,
            Cosabcli = request.Cosabcli,
            Tipo = request.Tipo,
            Cantidad = request.Cantidad,
            Instrumento = request.Instrumento,
            TipoOrden = request.TipoOrden,
            Precio = request.Precio,
            Monto = request.Monto,
            Vigencia = request.Vigencia,
            Mercado = mercado
        };

        await _propuestaRepository.AddAsync(propuesta, cancellationToken);

        var emailDelivered = true;
        try
        {
            var token = await _actionTokenService.CreateProposalReviewTokenAsync(
                representanteId,
                propuesta.PropuestaId,
                cancellationToken);
            var reviewLink =
                $"{_clientesFrontendUrl}/Clientes/propuestas/revision?token={Uri.EscapeDataString(token.Value)}";

            await _emailService.SendEmailAsync(
                request.CorreoCliente.Trim(),
                $"Resumen de tu {request.Tipo.Trim().ToLowerInvariant()} de {request.Instrumento.Trim()}",
                BuildEmailBody(propuesta.PropuestaId, request, mercado, reviewLink),
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            emailDelivered = false;
            _logger.LogError(
                exception,
                "La propuesta de cliente {PropuestaId} fue guardada, pero no se pudo enviar el correo de resumen.",
                propuesta.PropuestaId);
        }

        return new CreatePropuestaClienteResult(
            CreatePropuestaClienteStatus.Created,
            propuesta.PropuestaId,
            emailDelivered);
    }

    public async Task<PropuestaClienteReviewResult> GetReview(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        var token = await _actionTokenService.ValidateAsync(
            rawToken,
            ActionTokenService.ProposalReviewType,
            cancellationToken);

        if (token?.PropuestaId is not int propuestaId)
        {
            return new PropuestaClienteReviewResult(PropuestaClienteReviewStatus.InvalidToken);
        }

        var propuesta = await _propuestaRepository.GetByIdAsync(propuestaId, cancellationToken);
        return propuesta is null
            ? new PropuestaClienteReviewResult(PropuestaClienteReviewStatus.InvalidToken)
            : new PropuestaClienteReviewResult(PropuestaClienteReviewStatus.Valid, propuesta);
    }

    public async Task<PropuestaClienteDecisionResult> Decide(
        string rawToken,
        string requestedStatus,
        CancellationToken cancellationToken = default)
    {
        var estado = GetDecisionStatus(requestedStatus);
        if (estado is null)
        {
            return new PropuestaClienteDecisionResult(PropuestaClienteDecisionStatus.InvalidDecision);
        }

        var token = await _actionTokenService.ValidateAsync(
            rawToken,
            ActionTokenService.ProposalReviewType,
            cancellationToken);

        if (token?.PropuestaId is not int propuestaId)
        {
            return new PropuestaClienteDecisionResult(PropuestaClienteDecisionStatus.InvalidToken);
        }

        var propuesta = await _propuestaRepository.GetByIdAsync(propuestaId, cancellationToken);
        if (propuesta is null)
        {
            return new PropuestaClienteDecisionResult(PropuestaClienteDecisionStatus.InvalidToken);
        }

        if (!string.Equals(
                propuesta.Estado,
                PropuestaEstados.Pendiente,
                StringComparison.OrdinalIgnoreCase))
        {
            return new PropuestaClienteDecisionResult(
                PropuestaClienteDecisionStatus.AlreadyDecided,
                propuesta.PropuestaId,
                propuesta.Estado);
        }

        var updated = await _actionTokenService.TryApplyProposalDecisionAsync(
            token.TokenId,
            propuestaId,
            estado,
            cancellationToken);

        return updated
            ? new PropuestaClienteDecisionResult(
                PropuestaClienteDecisionStatus.Updated,
                propuestaId,
                estado)
            : new PropuestaClienteDecisionResult(PropuestaClienteDecisionStatus.InvalidToken);
    }

    private static string? GetDecisionStatus(string requestedStatus)
    {
        if (string.Equals(requestedStatus?.Trim(), PropuestaEstados.Aceptado, StringComparison.OrdinalIgnoreCase))
        {
            return PropuestaEstados.Aceptado;
        }

        return string.Equals(requestedStatus?.Trim(), PropuestaEstados.Cancelado, StringComparison.OrdinalIgnoreCase)
            ? PropuestaEstados.Cancelado
            : null;
    }

    private static string BuildEmailBody(
        int propuestaId,
        PropuestaClienteCreateRequest request,
        string mercado,
        string reviewLink)
    {
        static string Encode(string value) => WebUtility.HtmlEncode(value.Trim());
        static string FormatDecimal(decimal? value) => value.HasValue
            ? value.Value.ToString("N2", CultureInfo.GetCultureInfo("es-PE"))
            : "No aplica";

        var cantidad = request.Cantidad > 0 ? request.Cantidad.ToString("N0", CultureInfo.GetCultureInfo("es-PE")) : "No aplica";
        var precio = string.Equals(request.TipoOrden, "Mercado", StringComparison.OrdinalIgnoreCase)
            ? "A mercado"
            : $"{FormatDecimal(request.Precio)} {Encode(request.Moneda)}";
        var monto = request.Monto.HasValue
            ? $"{FormatDecimal(request.Monto)} {Encode(request.Moneda)}"
            : "No aplica";
        var safeReviewLink = WebUtility.HtmlEncode(reviewLink);

        return $$"""
            <!doctype html>
            <html lang="es">
            <body style="font-family:Arial,sans-serif;color:#202124;line-height:1.5">
              <h2 style="color:#17365d">Resumen de tu operación</h2>
              <p>Hemos recibido correctamente tu solicitud.</p>
              <table style="border-collapse:collapse;width:100%;max-width:600px">
                <tr><td style="padding:8px;border:1px solid #ddd"><strong>Número de propuesta</strong></td><td style="padding:8px;border:1px solid #ddd">{{propuestaId}}</td></tr>
                <tr><td style="padding:8px;border:1px solid #ddd"><strong>Operación</strong></td><td style="padding:8px;border:1px solid #ddd">{{Encode(request.Tipo)}}</td></tr>
                <tr><td style="padding:8px;border:1px solid #ddd"><strong>Instrumento</strong></td><td style="padding:8px;border:1px solid #ddd">{{Encode(request.Instrumento)}}</td></tr>
                <tr><td style="padding:8px;border:1px solid #ddd"><strong>Mercado</strong></td><td style="padding:8px;border:1px solid #ddd">{{Encode(mercado)}}</td></tr>
                <tr><td style="padding:8px;border:1px solid #ddd"><strong>Tipo de orden</strong></td><td style="padding:8px;border:1px solid #ddd">{{Encode(request.TipoOrden)}}</td></tr>
                <tr><td style="padding:8px;border:1px solid #ddd"><strong>Cantidad</strong></td><td style="padding:8px;border:1px solid #ddd">{{cantidad}}</td></tr>
                <tr><td style="padding:8px;border:1px solid #ddd"><strong>Precio</strong></td><td style="padding:8px;border:1px solid #ddd">{{precio}}</td></tr>
                <tr><td style="padding:8px;border:1px solid #ddd"><strong>Monto</strong></td><td style="padding:8px;border:1px solid #ddd">{{monto}}</td></tr>
                <tr><td style="padding:8px;border:1px solid #ddd"><strong>Moneda</strong></td><td style="padding:8px;border:1px solid #ddd">{{Encode(request.Moneda)}}</td></tr>
                <tr><td style="padding:8px;border:1px solid #ddd"><strong>Vigencia</strong></td><td style="padding:8px;border:1px solid #ddd">{{Encode(request.Vigencia)}}</td></tr>
                <tr><td style="padding:8px;border:1px solid #ddd"><strong>Código de cliente</strong></td><td style="padding:8px;border:1px solid #ddd">{{Encode(request.Cosabcli)}}</td></tr>
              </table>
              <p style="margin-top:24px">
                <a href="{{safeReviewLink}}" style="background:#17365d;color:#fff;padding:12px 18px;text-decoration:none;border-radius:4px;display:inline-block">
                  Revisar propuesta
                </a>
              </p>
              <p>Si el botón no funciona, copia y pega este enlace en tu navegador:</p>
              <p><a href="{{safeReviewLink}}">{{safeReviewLink}}</a></p>
              <p>Este correo confirma la recepción de tu solicitud; no constituye una confirmación de ejecución.</p>
            </body>
            </html>
            """;
    }
}

public enum CreatePropuestaClienteStatus
{
    Created,
    RepresentanteNotFound,
    CosabcliForbidden,
    InvalidMarket
}

public sealed record CreatePropuestaClienteResult(
    CreatePropuestaClienteStatus Status,
    int? PropuestaId = null,
    bool EmailDelivered = false);

public enum PropuestaClienteReviewStatus
{
    Valid,
    InvalidToken
}

public sealed record PropuestaClienteReviewResult(
    PropuestaClienteReviewStatus Status,
    Propuesta? Propuesta = null);

public enum PropuestaClienteDecisionStatus
{
    Updated,
    InvalidToken,
    InvalidDecision,
    AlreadyDecided
}

public sealed record PropuestaClienteDecisionResult(
    PropuestaClienteDecisionStatus Status,
    int? PropuestaId = null,
    string? Estado = null);
