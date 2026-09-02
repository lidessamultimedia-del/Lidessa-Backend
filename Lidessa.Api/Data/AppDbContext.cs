using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<PersonProfile> PersonProfiles => Set<PersonProfile>();

    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseEnrollment> CourseEnrollments => Set<CourseEnrollment>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<Lesson> Lessons => Set<Lesson>();

    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentAssignee> AssignmentAssignees => Set<AssignmentAssignee>();
    public DbSet<Submission> Submissions => Set<Submission>();

    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizAssignee> QuizAssignees => Set<QuizAssignee>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();

    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Certification> Certifications => Set<Certification>();

    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<PqrsfTicket> PqrsfTickets => Set<PqrsfTicket>();

    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
