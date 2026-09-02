namespace Lidessa.Api.Models;

public class AppUser
{
    public long Id { get; set; }
    public string? LegacyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int UnreadNotifications { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public PersonProfile? PersonProfile { get; set; }
    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
    public ICollection<PasswordResetCode> PasswordResetCodes { get; set; } = new List<PasswordResetCode>();
    public ICollection<Course> CoursesTaught { get; set; } = new List<Course>();
    public ICollection<CourseEnrollment> Enrollments { get; set; } = new List<CourseEnrollment>();
    public ICollection<AssignmentAssignee> AssignedAssignments { get; set; } = new List<AssignmentAssignee>();
    public ICollection<Submission> Submissions { get; set; } = new List<Submission>();
    public ICollection<QuizAssignee> AssignedQuizzes { get; set; } = new List<QuizAssignee>();
    public ICollection<QuizAttempt> QuizAttempts { get; set; } = new List<QuizAttempt>();
    public ICollection<LessonProgress> LessonProgress { get; set; } = new List<LessonProgress>();
    public ICollection<Message> SentMessages { get; set; } = new List<Message>();
    public ICollection<Message> ReceivedMessages { get; set; } = new List<Message>();
    public ICollection<Certification> Certifications { get; set; } = new List<Certification>();
    public ICollection<BlogPost> BlogPosts { get; set; } = new List<BlogPost>();
    public ICollection<PqrsfTicket> PqrsfTickets { get; set; } = new List<PqrsfTicket>();
}
