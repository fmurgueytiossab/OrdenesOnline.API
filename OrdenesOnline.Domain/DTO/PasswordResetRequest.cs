using System.ComponentModel.DataAnnotations;

namespace OrdenesOnline.Domain.DTO;

public sealed class PasswordResetRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;
}
