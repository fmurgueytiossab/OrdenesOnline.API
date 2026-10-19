namespace OrdenesOnline.Domain.entities;

public sealed record PropuestaSeguimientoSnapshot(
    bool RepresentanteExiste,
    IReadOnlyList<Propuesta> Propuestas,
    IReadOnlyDictionary<int, PropuestaFirmaSeguimiento>? Firmas = null);

public sealed class PropuestaFirmaSeguimiento
{
    public int PropuestaId { get; set; }
    public string EstadoSobre { get; set; } = string.Empty;
    public int FirmasRequeridas { get; set; }
    public int FirmasCompletadas { get; set; }
    public int TotalFirmantes { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool Completa => EstadoSobre == "completed" && FirmasRequeridas is 1 or 2 or 3
        && FirmasCompletadas == FirmasRequeridas && TotalFirmantes == FirmasRequeridas;
}

public enum MarcarPropuestaIngresadaStatus { Success, RepresentanteNotFound, NotFound, NotSigned }
