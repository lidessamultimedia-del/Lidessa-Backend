namespace Lidessa.Api.Dtos.Messages;

public class StaffConversationResponse
{
    public long CourseId { get; set; }
    public long OtherUserId { get; set; }
    public MessageResponse LastMessage { get; set; } = null!;
    public int UnreadCount { get; set; }
}
