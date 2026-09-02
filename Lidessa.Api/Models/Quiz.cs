namespace Lidessa.Api.Models;

public class Quiz
{
    public long Id { get; set; }
    public string? LegacyId { get; set; }
    public long CourseId { get; set; }
    public long? TopicId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public DateOnly? PublishAt { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public int SortOrder { get; set; }

    public Course Course { get; set; } = null!;
    public Topic? Topic { get; set; }
    public ICollection<QuizQuestion> Questions { get; set; } = new List<QuizQuestion>();
    public ICollection<QuizAssignee> Assignees { get; set; } = new List<QuizAssignee>();
    public ICollection<QuizAttempt> Attempts { get; set; } = new List<QuizAttempt>();
}
