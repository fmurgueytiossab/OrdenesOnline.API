using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

public sealed class PropuestaClienteService
{
    private readonly IPropuestaRepository _propuestaRepository;
    private readonly IRepresentanteRepository _representanteRepository;
    private readonly IRepresentanteClientScopeRepository _clientScopeRepository;
    private readonly IDocuSignPropuestas _docuSign;
    private readonly IPropuestaDocuSignRepository _docuSignRepository;
    private readonly ActionTokenService _actionTokenService;
    private readonly MarketHoursService _marketHours;

    public PropuestaClienteService(
        IPropuestaRepository propuestaRepository,
        IRepresentanteRepository representanteRepository,
        IRepresentanteClientScopeRepository clientScopeRepository,
        IDocuSignPropuestas docuSign,
        IPropuestaDocuSignRepository docuSignRepository,
        ActionTokenService actionTokenService,
        MarketHoursService marketHours)
    {
        _propuestaRepository = propuestaRepository;
        _representanteRepository = representanteRepository;
        _clientScopeRepository = clientScopeRepository;
        _docuSign = docuSign;
        _docuSignRepository = docuSignRepository;
        _actionTokenService = actionTokenService;
        _marketHours = marketHours;
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

        var hours = _marketHours.Get();
        if (!_marketHours.TryResolveValidity(mercado, request.Vigencia, hours, out var validity))
            return new(CreatePropuestaClienteStatus.InvalidValidity);
        request.Vigencia = validity;

        DocuSignPreparacion preparation;
        try { preparation = await _docuSign.PrepareAsync(request.Cosabcli, cancellationToken); }
        catch (DocuSignContactException ex)
        {
            return new(CreatePropuestaClienteStatus.InvalidSigners, Advertencia: ex.Message);
        }

        var propuesta = new Propuesta
        {
            NombreOperador = representante.Nombre,
            CorreoCorporativo = representante.CorreoCorporativo,
            Cosabcli = request.Cosabcli.Trim(),
            Tipo = request.Tipo,
            Cantidad = request.Cantidad,
            Instrumento = request.Instrumento,
            TipoOrden = request.TipoOrden,
            Precio = request.Precio,
            Monto = request.Monto,
            Vigencia = request.Vigencia,
            Mercado = mercado,
            Estado = PropuestaEstados.Pendiente
        };

        await _propuestaRepository.AddAsync(propuesta, cancellationToken);

        var envio = await _docuSign.SendAsync(propuesta, request, preparation, cancellationToken);
        return new(CreatePropuestaClienteStatus.Created, propuesta.PropuestaId,
            EnvelopeId: envio.EnvelopeId, DocuSignEnviado: envio.Enviado, Advertencia: envio.Advertencia);
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

        if (await _docuSignRepository.HasAsync(propuestaId, cancellationToken))
            return new(PropuestaClienteReviewStatus.InvalidToken);

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

        if (await _docuSignRepository.HasAsync(propuestaId, cancellationToken))
            return new(PropuestaClienteDecisionStatus.InvalidToken);

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
}

public enum CreatePropuestaClienteStatus
{
    InvalidSigners,
    InvalidValidity,
    Created,
    RepresentanteNotFound,
    CosabcliForbidden,
    InvalidMarket
}

public sealed record CreatePropuestaClienteResult(
    CreatePropuestaClienteStatus Status,
    int? PropuestaId = null,
    bool EmailDelivered = false,
    Guid? EnvelopeId = null,
    bool DocuSignEnviado = false,
    string? Advertencia = null);

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
