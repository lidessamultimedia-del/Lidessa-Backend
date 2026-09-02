namespace Lidessa.Api.Models;

public class Topic
{
    public long Id { get; set; }
    public string? LegacyId { get; set; }
    public long CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public Course Course { get; set; } = null!;
    public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
    public ICollection<Quiz> Quizzes { get; set; } = new List<Quiz>();
}
