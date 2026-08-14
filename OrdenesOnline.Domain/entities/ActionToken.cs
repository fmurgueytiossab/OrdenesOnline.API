using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OrdenesOnline.Domain.entities;

[Table("Token")]
public sealed class ActionToken
{
    [Key]
    public int TokenId { get; set; }

    public int UserId { get; set; }

    public int? PropuestaId { get; set; }

    [StringLength(256)]
    public string TokenHash { get; set; } = string.Empty;

    [StringLength(20)]
    public string Type { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public bool IsUsed { get; set; }

    public bool IsRevoked { get; set; }
}

public static class ActionTokenTypes
{
    public const string PasswordReset = "password_reset";
    public const string ProposalReview = "proposal_review";
}
