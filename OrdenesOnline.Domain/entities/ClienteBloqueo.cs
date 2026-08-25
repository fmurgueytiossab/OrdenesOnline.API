using System.ComponentModel.DataAnnotations.Schema;

namespace OrdenesOnline.Domain.entities;

public sealed class ClienteBloqueo
{
    [Column("cosabcli")]
    public string Cosabcli { get; set; } = string.Empty;

    [Column("glosa")]
    public string? Glosa { get; set; }
}
