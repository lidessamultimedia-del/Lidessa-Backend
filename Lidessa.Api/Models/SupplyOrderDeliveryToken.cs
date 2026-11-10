namespace Lidessa.Api.Models;

// Token de un solo uso para que el cliente confirme la entrega de su pedido
// con un clic desde el correo de envío, sin necesidad de iniciar sesión —
// mismo patrón que AccountInviteToken/PasswordResetCode.
public class SupplyOrderDeliveryToken
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public SupplyOrder Order { get; set; } = null!;
}
