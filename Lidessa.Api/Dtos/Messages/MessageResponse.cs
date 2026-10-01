namespace Lidessa.Api.Dtos.Messages;

public class MessageResponse
{
    public long Id { get; set; }
    public long CourseId { get; set; }
    public long FromUserId { get; set; }
    public long ToUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}
