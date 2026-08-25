using System.ComponentModel.DataAnnotations.Schema;

namespace OrdenesOnline.Domain.entities;

public sealed class ClienteApoderado
{
    [Column("cosabcli")]
    public string Cosabcli { get; set; } = string.Empty;

    [Column("nucel")]
    public string? Nucel { get; set; }
}
