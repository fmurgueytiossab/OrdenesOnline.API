using System.ComponentModel.DataAnnotations;

namespace OrdenesOnline.Domain.DTO;

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Correo { get; set; } = string.Empty;

    [Required, StringLength(256)]
    public string Password { get; set; } = string.Empty;
}
