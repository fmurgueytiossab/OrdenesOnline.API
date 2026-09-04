namespace OrdenesOnline.Domain.entities;

public sealed record RepresentanteClientScope(
    bool RepresentanteExiste,
    IReadOnlyList<string> Gestores,
    IReadOnlyList<string> Cosabcli);
