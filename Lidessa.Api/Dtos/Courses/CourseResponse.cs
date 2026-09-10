namespace Lidessa.Api.Dtos.Courses;

public class CourseResponse
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public long? TeacherId { get; set; }
    public string? TeacherName { get; set; }
    public string Format { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public string? Color { get; set; }
    public string? Image { get; set; }
    public bool RequiresPassword { get; set; }
    public bool SelfEnrollment { get; set; }
    public bool GuestAccess { get; set; }
    public bool Published { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
