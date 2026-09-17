namespace Lidessa.Api.Dtos.Assignments;

public class AssignmentResponse
{
    public long Id { get; set; }
    public long CourseId { get; set; }
    public long? TopicId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public decimal MaxScore { get; set; }
    public DateOnly? PublishAt { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentUrl { get; set; }
    public long? AttachmentSizeBytes { get; set; }
    public List<long> AssignedStudentIds { get; set; } = new();
}
