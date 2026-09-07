using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Auth;

public class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
