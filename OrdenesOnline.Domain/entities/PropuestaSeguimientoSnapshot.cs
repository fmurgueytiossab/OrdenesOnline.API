namespace OrdenesOnline.Domain.entities;

public sealed record PropuestaSeguimientoSnapshot(
    bool RepresentanteExiste,
    IReadOnlyList<Propuesta> Propuestas);
