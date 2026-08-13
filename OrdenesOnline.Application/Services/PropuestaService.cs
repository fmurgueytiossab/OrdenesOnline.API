using Microsoft.Extensions.Logging;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

public sealed class PropuestaService
{
    private readonly IPropuestaRepository _propuestaRepository;
    private readonly IRepresentanteRepository _representanteRepository;
    private readonly ZapierService _zapierService;
    private readonly ILogger<PropuestaService> _logger;

    public PropuestaService(
        IPropuestaRepository propuestaRepository,
        IRepresentanteRepository representanteRepository,
        ZapierService zapierService,
        ILogger<PropuestaService> logger)
    {
        _propuestaRepository = propuestaRepository;
        _representanteRepository = representanteRepository;
        _zapierService = zapierService;
        _logger = logger;
    }

    public async Task<CreatePropuestaResult> Create(
        int representanteId,
        PropuestaCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var representante = await _representanteRepository.GetByIdAsync(representanteId, cancellationToken);

        if (representante is null)
        {
            return new CreatePropuestaResult(CreatePropuestaStatus.RepresentanteNotFound);
        }

        if (!representante.Cosabcli.Contains(request.Cosabcli, StringComparer.OrdinalIgnoreCase))
        {
            return new CreatePropuestaResult(CreatePropuestaStatus.CosabcliForbidden);
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
            Mercado = request.Mercado
        };

        await _propuestaRepository.AddAsync(propuesta, cancellationToken);

        var notificationDelivered = true;
        try
        {
            await _zapierService.EnviarPropuestaCreada(
                propuesta,
                representante.Dni,
                request.Moneda,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException ||
            exception is TimeoutException ||
            exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            notificationDelivered = false;
            _logger.LogError(
                exception,
                "La propuesta {PropuestaId} fue guardada, pero no se pudo notificar a Zapier.",
                propuesta.PropuestaId);
        }

        return new CreatePropuestaResult(
            CreatePropuestaStatus.Created,
            propuesta.PropuestaId,
            notificationDelivered);
    }
}

public enum CreatePropuestaStatus
{
    Created,
    RepresentanteNotFound,
    CosabcliForbidden
}

public sealed record CreatePropuestaResult(
    CreatePropuestaStatus Status,
    int? PropuestaId = null,
    bool NotificationDelivered = false);
