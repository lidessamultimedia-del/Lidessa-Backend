using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Lessons;

public class LessonRequest
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string? Content { get; set; }

    public long? TopicId { get; set; }

    public DateOnly? PublishAt { get; set; }

    [MaxLength(260)]
    public string? AttachmentFileName { get; set; }

    [MaxLength(500)]
    public string? AttachmentUrl { get; set; }

    public long? AttachmentSizeBytes { get; set; }
}
