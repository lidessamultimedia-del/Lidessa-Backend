namespace Lidessa.Api.Models;

// Cuentas vinculadas (ver 2.2): una misma cuenta (un correo, una contraseña)
// puede tener varios roles — p. ej. estudiante del CEET y cliente de la
// tienda de Suministros. Esta tabla guarda TODOS los roles de la cuenta,
// incluido el original. AppUser.Role se conserva como rol principal (el que
// la cuenta tuvo al crearse) para no romper lo que todavía lee un solo rol.
public class AppUserRole
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }

    // Quién agregó el rol (admin). NULL = rol original de la cuenta, puesto
    // al registrarse/invitarse o por el backfill de la migración.
    public long? GrantedByUserId { get; set; }

    public AppUser User { get; set; } = null!;
    public AppUser? GrantedByUser { get; set; }
}
