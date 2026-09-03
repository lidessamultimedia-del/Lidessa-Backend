using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Auth;

public class RegisterRequest
{
    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;
}
