using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

public sealed class PropuestaSeguimientoService
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;
    private readonly IPropuestaSeguimientoRepository _repository;

    public PropuestaSeguimientoService(IPropuestaSeguimientoRepository repository) => _repository = repository;

    public async Task<PropuestaSeguimientoResult> Get(
        int representanteId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaximumPageSize)
            return new(PropuestaSeguimientoStatus.InvalidPagination);

        var snapshot = await _repository.GetAsync(representanteId, cancellationToken);
        if (!snapshot.RepresentanteExiste)
            return new(PropuestaSeguimientoStatus.RepresentanteNotFound);

        var (start, end) = PropuestaTrackingDay.GetUtcRange(DateTimeOffset.UtcNow);
        var proposals = snapshot.Propuestas
            .Where(p => p.FechaRegistro >= start && p.FechaRegistro < end)
            .OrderBy(p => GetStatusPriority(GetStatus(p.Estado, snapshot.Firmas?.GetValueOrDefault(p.PropuestaId))))
            .ThenByDescending(p => p.FechaRegistro)
            .ThenByDescending(p => p.PropuestaId)
            .ToList();
        var offset = (int)Math.Min((long)(page - 1) * pageSize, proposals.Count);
        var items = proposals.Skip(offset).Take(pageSize).Select(p =>
        {
            var local = PropuestaTrackingDay.ToPeru(p.FechaRegistro);
            var firma = snapshot.Firmas?.GetValueOrDefault(p.PropuestaId);
            var estado = GetStatus(p.Estado, firma);
            return new PropuestaSeguimientoItem(
                p.PropuestaId, p.Cosabcli.Trim(), DateOnly.FromDateTime(local),
                TimeOnly.FromDateTime(local), string.Empty, p.Instrumento.Trim(),
                p.Tipo.Trim().ToUpperInvariant() is "V" or "VENTA" ? "V" : "C",
                p.Cantidad, 0m, 0m, p.Cantidad, p.Precio, estado, GetChannel(p.Mercado), estado == "FIRMADO");
        }).ToList();

        return new(PropuestaSeguimientoStatus.Success,
            new PropuestaSeguimientoPage(items, page, pageSize, proposals.Count, DateTimeOffset.UtcNow));
    }

    public Task<MarcarPropuestaIngresadaStatus> MarkEntered(
        int representanteId, int propuestaId, CancellationToken cancellationToken = default) =>
        _repository.MarkEnteredAsync(representanteId, propuestaId, cancellationToken);

    private static string GetStatus(string status, PropuestaFirmaSeguimiento? firma)
    {
        if (status == PropuestaEstados.Ingresado) return "INGRESADO";
        if (status == PropuestaEstados.Cancelado || firma?.EstadoSobre is "declined" or "voided") return "CANCELADO";
        if (status == PropuestaEstados.Aceptado)
            return firma?.Completa == true ? "FIRMADO" : "PENDIENTE";
        if (firma?.EstadoSobre is "sent" or "delivered" or "signed") return "ENVIADO";
        return status == PropuestaEstados.Pendiente ? "PENDIENTE" : "DESCONOCIDO";
    }

    private static int GetStatusPriority(string status) => status switch
    {
        "ENVIADO" => 0,
        "FIRMADO" => 1,
        "CANCELADO" => 2,
        "INGRESADO" => 4,
        _ => 3
    };

    private static string GetChannel(string market) => market.Trim().ToUpperInvariant() switch
    {
        "01" or "BVL" or "LOCAL" => "BVL",
        "98" or "CANACCORD" or "CANACCORD RENTA4" => "CANACCORD",
        "16" or "EUROCLEAR" => "EUROCLEAR",
        _ => market.Trim().ToUpperInvariant()
    };
}

public enum PropuestaSeguimientoStatus { Success, InvalidPagination, RepresentanteNotFound }
public sealed record PropuestaSeguimientoResult(
    PropuestaSeguimientoStatus Status, PropuestaSeguimientoPage? Page = null);
