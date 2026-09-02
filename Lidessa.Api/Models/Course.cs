namespace Lidessa.Api.Models;

public class Course
{
    public long Id { get; set; }
    public string? LegacyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public long? TeacherId { get; set; }
    public string Format { get; set; } = "topics";
    public bool Published { get; set; }
    public bool Visible { get; set; } = true;
    public bool Listed { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool CompletionTrackingEnabled { get; set; } = true;
    public bool RequiresPassword { get; set; }
    public string? PasswordHash { get; set; }
    public bool SelfEnrollment { get; set; }
    public bool GuestAccess { get; set; }
    public int? Capacity { get; set; }
    public string? Color { get; set; }
    public string? ImageUrl { get; set; }
    public string? Duration { get; set; }
    public string? Modality { get; set; }
    public bool Certified { get; set; }
    public string? Intro { get; set; }
    public string? ObjectivesJson { get; set; }
    public string? ModulesJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AppUser? Teacher { get; set; }
    public ICollection<CourseEnrollment> Enrollments { get; set; } = new List<CourseEnrollment>();
    public ICollection<Topic> Topics { get; set; } = new List<Topic>();
    public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
    public ICollection<Quiz> Quizzes { get; set; } = new List<Quiz>();
    public ICollection<LessonProgress> LessonProgress { get; set; } = new List<LessonProgress>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<Certification> Certifications { get; set; } = new List<Certification>();
}
