namespace Lidessa.Api.Models;

public class LessonProgress
{
    public long StudentId { get; set; }
    public long CourseId { get; set; }
    public long LessonId { get; set; }
    public DateTime CompletedAt { get; set; }

    public AppUser Student { get; set; } = null!;
    public Course Course { get; set; } = null!;
    public Lesson Lesson { get; set; } = null!;
}
