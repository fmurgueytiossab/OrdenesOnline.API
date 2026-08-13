using System.ComponentModel.DataAnnotations;

namespace OrdenesOnline.Domain.DTO;

public sealed class UpdatePasswordRequest
{
    [Required, StringLength(2048)]
    public string Token { get; set; } = string.Empty;

    [Required, StringLength(256, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
}
