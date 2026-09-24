namespace Lidessa.Api.Dtos.Quizzes;

public class QuizResponse
{
    public long Id { get; set; }
    public long CourseId { get; set; }
    public long? TopicId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public DateOnly? PublishAt { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public int SortOrder { get; set; }
    public List<long> AssignedStudentIds { get; set; } = new();
    public List<QuizQuestionResponse> Questions { get; set; } = new();
}
