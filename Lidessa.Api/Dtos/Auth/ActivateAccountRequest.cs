using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Auth;

public class ActivateAccountRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, MinLength(6)]
    public string Password { get; set; } = string.Empty;
}
