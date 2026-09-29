namespace Lidessa.Api.Dtos.Progress;

public class CourseProgressResponse
{
    public List<long> CompletedLessonIds { get; set; } = new();
    public int LessonsCompleted { get; set; }
    public int LessonsTotal { get; set; }
    public int AvanceCompleted { get; set; }
    public int AvanceTotal { get; set; }
    public int AvancePercent { get; set; }
}
