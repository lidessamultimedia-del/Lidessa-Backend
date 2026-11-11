namespace Lidessa.Api.Dtos.Auth;

public class UserResponse
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    // Rol principal (compatibilidad con el frontend actual).
    public string Role { get; set; } = string.Empty;
    // Todos los roles de la cuenta (cuentas vinculadas, ver 2.2), con el
    // principal primero.
    public List<string> Roles { get; set; } = new();
    public string Phone { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}
