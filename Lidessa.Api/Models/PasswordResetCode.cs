namespace Lidessa.Api.Models;

public class PasswordResetCode
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public AppUser User { get; set; } = null!;
}
