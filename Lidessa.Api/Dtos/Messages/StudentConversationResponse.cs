namespace Lidessa.Api.Dtos.Messages;

public class StudentConversationResponse
{
    public long CourseId { get; set; }
    public long OtherUserId { get; set; }
    public MessageResponse? LastMessage { get; set; }
    public int UnreadCount { get; set; }
}
