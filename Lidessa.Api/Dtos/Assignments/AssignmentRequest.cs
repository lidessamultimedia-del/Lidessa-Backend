using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Assignments;

public class AssignmentRequest
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public DateTime DueDate { get; set; }

    public long? TopicId { get; set; }

    public DateOnly? PublishAt { get; set; }

    [MaxLength(260)]
    public string? AttachmentFileName { get; set; }

    [MaxLength(500)]
    public string? AttachmentUrl { get; set; }

    public long? AttachmentSizeBytes { get; set; }

    // Vacío = para todo el curso; con valores = solo esos estudiantes.
    public List<long> AssignedStudentIds { get; set; } = new();
}
