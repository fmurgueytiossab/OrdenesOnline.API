using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OrdenesOnline.Domain.entities;

public sealed class Cliente
{
    public static bool IsJointAccount(string? value)
    {
        var normalizedValue = value?.Trim();
        return string.Equals(normalizedValue, "S", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedValue, "M", StringComparison.OrdinalIgnoreCase);
    }

    [Key]
    [Column("cosabcli")]
    public string Cosabcli { get; set; } = string.Empty;

    [Column("apepat")]
    public string? Apepat { get; set; }

    [Column("apemat")]
    public string? Apemat { get; set; }

    [Column("nombres")]
    public string? Nombres { get; set; }

    [Column("emailcli")]
    public string? Emailcli { get; set; }

    [Column("nucel")]
    public string? Nucel { get; set; }

    [Column("fg_mancomunado")]
    public string? FgMancomunado { get; set; }

    [Column("descli")]
    public string? Descli { get; set; }

    [Column("estado")]
    public string? Estado { get; set; }

    [Column("gestor")]
    public string? Gestor { get; set; }
}
