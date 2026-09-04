namespace OrdenesOnline.Domain.DTO;

public sealed record PropuestaBvlSeguimientoItem(
    int CodigoOrden,
    string Cosabcli,
    DateOnly FechaPropuesta,
    TimeOnly? HoraPropuesta,
    string NumeroPropuestaBvl,
    string Instrumento,
    string Tipo,
    decimal CantidadPropuesta,
    decimal CantidadEjecutada,
    decimal CantidadAnulada,
    decimal CantidadPendiente,
    decimal? Precio,
    string Estado,
    string Mercado);

public sealed record PropuestaBvlSeguimientoPage(
    IReadOnlyList<PropuestaBvlSeguimientoItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    DateTimeOffset LastUpdatedAt);
