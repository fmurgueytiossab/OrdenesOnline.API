using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

public sealed class PropuestaBvlSeguimientoService
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;

    private readonly IPropuestaBvlSeguimientoRepository _repository;

    public PropuestaBvlSeguimientoService(IPropuestaBvlSeguimientoRepository repository)
    {
        _repository = repository;
    }

    public async Task<PropuestaBvlSeguimientoResult> Get(
        int representanteId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaximumPageSize)
        {
            return new PropuestaBvlSeguimientoResult(
                PropuestaBvlSeguimientoStatus.InvalidPagination);
        }

        var snapshot = await _repository.GetAsync(representanteId, cancellationToken);
        if (!snapshot.RepresentanteExiste)
        {
            return new PropuestaBvlSeguimientoResult(
                PropuestaBvlSeguimientoStatus.RepresentanteNotFound);
        }

        var operacionesBvlPorClave = snapshot.Operaciones
            .GroupBy(CreateMatchKey)
            .ToDictionary(
                group => group.Key,
                group => new Queue<OperacionBvl>(group
                    .OrderByDescending(item => item.FechaPropuesta)
                    .ThenByDescending(item => item.HoraPropuesta)
                    .ThenByDescending(item => item.NumeroPropuesta, StringComparer.OrdinalIgnoreCase)));
        var operacionesExteriorPorClave = snapshot.OperacionesExterior
            .GroupBy(CreateExternalMatchKey)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(item => item.NumeroOperacion, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        var items = new List<PropuestaBvlSeguimientoItem>();

        foreach (var propuesta in snapshot.Propuestas.OrderByDescending(item => item.FechaRegistro))
        {
            var channel = GetChannel(propuesta.Mercado);
            if (channel == "BVL")
            {
                if (!operacionesBvlPorClave.TryGetValue(CreateMatchKey(propuesta), out var coincidencias) ||
                    coincidencias.Count == 0)
                {
                    continue;
                }

                var operacion = coincidencias.Dequeue();
                var cantidadPendiente = Math.Max(
                    operacion.CantidadPropuesta -
                    operacion.CantidadEjecutada -
                    operacion.CantidadAnulada,
                    0m);

                items.Add(new PropuestaBvlSeguimientoItem(
                    propuesta.PropuestaId,
                    operacion.Cosabcli.Trim(),
                    operacion.FechaPropuesta,
                    operacion.HoraPropuesta,
                    operacion.NumeroPropuesta.Trim(),
                    operacion.Instrumento.Trim(),
                    NormalizeTradeSide(operacion.Tipo),
                    operacion.CantidadPropuesta,
                    operacion.CantidadEjecutada,
                    operacion.CantidadAnulada,
                    cantidadPendiente,
                    operacion.Precio,
                    GetStatus(operacion),
                    channel));
                continue;
            }

            if (channel is not ("CANACCORD" or "VIEWTRADE") &&
                !string.Equals(NormalizeMarket(propuesta.Mercado), "EXTRANJERO", StringComparison.Ordinal))
            {
                continue;
            }

            if (!operacionesExteriorPorClave.TryGetValue(
                    CreateExternalMatchKey(propuesta),
                    out var coincidenciasExterior))
            {
                continue;
            }

            var matchIndex = coincidenciasExterior.FindIndex(operacion =>
                (channel is null || GetChannel(operacion.BrokerCode) == channel) &&
                (!propuesta.Precio.HasValue || operacion.Precio == propuesta.Precio));
            if (matchIndex < 0)
            {
                continue;
            }

            var operacionExterior = coincidenciasExterior[matchIndex];
            coincidenciasExterior.RemoveAt(matchIndex);
            var externalChannel = GetChannel(operacionExterior.BrokerCode)!;

            items.Add(new PropuestaBvlSeguimientoItem(
                propuesta.PropuestaId,
                operacionExterior.Cosabcli.Trim(),
                operacionExterior.FechaOperacion,
                null,
                operacionExterior.NumeroOperacion.Trim(),
                operacionExterior.Instrumento.Trim(),
                NormalizeTradeSide(operacionExterior.Tipo),
                operacionExterior.Cantidad,
                0m,
                0m,
                operacionExterior.Cantidad,
                operacionExterior.Precio,
                PropuestaBvlEstados.Pendiente,
                externalChannel));
        }

        var orderedItems = items
            .OrderByDescending(item => item.FechaPropuesta)
            .ThenByDescending(item => item.HoraPropuesta)
            .ThenByDescending(item => item.NumeroOperacion, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totalCount = orderedItems.Count;
        var requestedOffset = (long)(page - 1) * pageSize;
        var offset = (int)Math.Min(requestedOffset, totalCount);
        var pageItems = orderedItems
            .Skip(offset)
            .Take(pageSize)
            .ToList();

        return new PropuestaBvlSeguimientoResult(
            PropuestaBvlSeguimientoStatus.Success,
            new PropuestaBvlSeguimientoPage(
                pageItems,
                page,
                pageSize,
                totalCount,
                DateTimeOffset.UtcNow));
    }

    internal static string GetStatus(OperacionBvl operacion)
    {
        if (operacion.CantidadPropuesta > 0 &&
            operacion.CantidadEjecutada >= operacion.CantidadPropuesta)
        {
            return PropuestaBvlEstados.Ejecutada;
        }

        // Si hubo alguna ejecución, PARCIAL conserva esa información incluso
        // cuando todo el saldo restante haya sido anulado.
        if (operacion.CantidadEjecutada > 0)
        {
            return PropuestaBvlEstados.Parcial;
        }

        if (operacion.CantidadPropuesta > 0 &&
            operacion.CantidadAnulada >= operacion.CantidadPropuesta)
        {
            return PropuestaBvlEstados.Anulada;
        }

        return PropuestaBvlEstados.Pendiente;
    }

    private static PropuestaMatchKey CreateMatchKey(Propuesta propuesta) => new(
        Normalize(propuesta.Cosabcli),
        NormalizeTradeSide(propuesta.Tipo),
        Normalize(propuesta.Instrumento),
        propuesta.Cantidad,
        propuesta.Precio,
        DateOnly.FromDateTime(propuesta.FechaRegistro));

    private static PropuestaMatchKey CreateMatchKey(OperacionBvl operacion) => new(
        Normalize(operacion.Cosabcli),
        NormalizeTradeSide(operacion.Tipo),
        Normalize(operacion.Instrumento),
        operacion.CantidadPropuesta,
        operacion.Precio,
        operacion.FechaPropuesta);

    private static ExternalMatchKey CreateExternalMatchKey(Propuesta propuesta) => new(
        Normalize(propuesta.Cosabcli),
        NormalizeTradeSide(propuesta.Tipo),
        Normalize(propuesta.Instrumento),
        propuesta.Cantidad,
        DateOnly.FromDateTime(propuesta.FechaRegistro));

    private static ExternalMatchKey CreateExternalMatchKey(OperacionExterior operacion) => new(
        Normalize(operacion.Cosabcli),
        NormalizeTradeSide(operacion.Tipo),
        Normalize(operacion.Instrumento),
        operacion.Cantidad,
        operacion.FechaOperacion);

    private static string Normalize(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static string NormalizeTradeSide(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "COMPRA" => "C",
            "VENTA" => "V",
            _ => normalized
        };
    }

    private static string NormalizeMarket(string? value) =>
        new(Normalize(value).Where(char.IsLetterOrDigit).ToArray());

    private static string? GetChannel(string? marketOrBrokerCode) =>
        NormalizeMarket(marketOrBrokerCode) switch
        {
            "BVL" => "BVL",
            "57" or "CANACCORD" or "CANACCORDRENTA4" => "CANACCORD",
            "66" or "VIEWTRADE" or "PERSHING" => "VIEWTRADE",
            _ => null
        };

    private readonly record struct PropuestaMatchKey(
        string Cosabcli,
        string Tipo,
        string Instrumento,
        decimal Cantidad,
        decimal? Precio,
        DateOnly Fecha);

    private readonly record struct ExternalMatchKey(
        string Cosabcli,
        string Tipo,
        string Instrumento,
        decimal Cantidad,
        DateOnly Fecha);
}

public static class PropuestaBvlEstados
{
    public const string Ejecutada = "EJECUTADA";
    public const string Anulada = "ANULADA";
    public const string Parcial = "PARCIAL";
    public const string Pendiente = "PENDIENTE";
}

public enum PropuestaBvlSeguimientoStatus
{
    Success,
    InvalidPagination,
    RepresentanteNotFound
}

public sealed record PropuestaBvlSeguimientoResult(
    PropuestaBvlSeguimientoStatus Status,
    PropuestaBvlSeguimientoPage? Page = null);
