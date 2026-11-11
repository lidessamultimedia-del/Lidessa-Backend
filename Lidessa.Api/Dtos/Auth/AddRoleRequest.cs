using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Auth;

public class AddRoleRequest
{
    [Required]
    public string Role { get; set; } = string.Empty;
}
