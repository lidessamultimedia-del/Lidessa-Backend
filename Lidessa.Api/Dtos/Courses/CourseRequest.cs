using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Courses;

public class CourseRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? ShortName { get; set; }

    [MaxLength(300)]
    public string? Description { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    public long? TeacherId { get; set; }

    [MaxLength(20)]
    public string Format { get; set; } = "topics";

    [Range(1, 10000)]
    public int Capacity { get; set; }

    [MaxLength(10)]
    public string? Color { get; set; }

    [MaxLength(500)]
    public string? Image { get; set; }

    public bool RequiresPassword { get; set; }

    public string? Password { get; set; }

    public bool SelfEnrollment { get; set; }

    public bool GuestAccess { get; set; }

    public bool Published { get; set; }
}
