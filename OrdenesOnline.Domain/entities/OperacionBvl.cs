namespace OrdenesOnline.Domain.entities;

public sealed record OperacionBvl(
    string Cosabcli,
    DateOnly FechaPropuesta,
    TimeOnly? HoraPropuesta,
    string NumeroPropuesta,
    string Instrumento,
    decimal CantidadEjecutada,
    decimal CantidadAnulada,
    string Tipo,
    decimal CantidadPropuesta,
    decimal? Precio);

public sealed record OperacionExterior(
    string BrokerCode,
    string NumeroOperacion,
    DateOnly FechaOperacion,
    string Instrumento,
    string Tipo,
    decimal Cantidad,
    decimal? Precio,
    string Cosabcli);

public sealed record PropuestaBvlSeguimientoSnapshot(
    bool RepresentanteExiste,
    IReadOnlyList<Propuesta> Propuestas,
    IReadOnlyList<OperacionBvl> Operaciones,
    IReadOnlyList<OperacionExterior> OperacionesExterior);
