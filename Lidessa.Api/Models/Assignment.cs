namespace Lidessa.Api.Models;

public class Assignment
{
    public long Id { get; set; }
    public string? LegacyId { get; set; }
    public long CourseId { get; set; }
    public long? TopicId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public decimal MaxScore { get; set; } = 10.0m;
    public DateOnly? PublishAt { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentUrl { get; set; }
    public long? AttachmentSizeBytes { get; set; }

    public Course Course { get; set; } = null!;
    public Topic? Topic { get; set; }
    public ICollection<AssignmentAssignee> Assignees { get; set; } = new List<AssignmentAssignee>();
    public ICollection<Submission> Submissions { get; set; } = new List<Submission>();
}
