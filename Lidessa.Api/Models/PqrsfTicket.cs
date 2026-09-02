namespace Lidessa.Api.Models;

public class PqrsfTicket
{
    public long Id { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    public string TicketType { get; set; } = string.Empty;
    public string FromName { get; set; } = "Anónimo";
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string MessageBody { get; set; } = string.Empty;
    public DateOnly TicketDate { get; set; }
    public string Status { get; set; } = "Pendiente";
    public string? Response { get; set; }
    public DateTime? RespondedAt { get; set; }
    public bool EmailSent { get; set; }
    public long? AccountId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }

    public AppUser? Account { get; set; }
}
