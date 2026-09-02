namespace Lidessa.Api.Models;

public class Lesson
{
    public long Id { get; set; }
    public string? LegacyId { get; set; }
    public long CourseId { get; set; }
    public long? TopicId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateOnly? PublishAt { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentUrl { get; set; }
    public long? AttachmentSizeBytes { get; set; }

    public Course Course { get; set; } = null!;
    public Topic? Topic { get; set; }
    public ICollection<LessonProgress> ProgressEntries { get; set; } = new List<LessonProgress>();
}
