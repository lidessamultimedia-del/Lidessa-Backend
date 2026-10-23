using System.Net.Http.Json;
using Lidessa.Api.Models;

namespace Lidessa.Api.Tests.Infrastructure;

// Escenario base de cada prueba: un curso con su profesor y dos estudiantes
// inscritos, más un segundo curso de otro profesor (para probar pertenencia)
// y un estudiante que no está inscrito en nada.
public abstract class LmsTestBase : IAsyncLifetime
{
    protected LmsApiFactory Api { get; } = new();

    protected AppUser Admin { get; } = User(1, "admin");
    protected AppUser Teacher { get; } = User(2, "profesor");
    protected AppUser OtherTeacher { get; } = User(3, "profesor");
    protected AppUser Student { get; } = User(10, "estudiante");
    protected AppUser Classmate { get; } = User(11, "estudiante");
    protected AppUser Outsider { get; } = User(12, "estudiante");

    protected const long CourseId = 100;
    protected const long OtherCourseId = 200;
    protected const long TopicId = 1000;
    protected const long OtherCourseTopicId = 2000;

    public async Task InitializeAsync()
    {
        await Api.SeedAsync(db =>
        {
            db.Users.AddRange(Admin, Teacher, OtherTeacher, Student, Classmate, Outsider);
            db.Courses.AddRange(
                new Course { Id = CourseId, Name = "Liderazgo", TeacherId = Teacher.Id },
                new Course { Id = OtherCourseId, Name = "SG-SST", TeacherId = OtherTeacher.Id });
            db.CourseEnrollments.AddRange(
                new CourseEnrollment { CourseId = CourseId, StudentId = Student.Id },
                new CourseEnrollment { CourseId = CourseId, StudentId = Classmate.Id });
            db.Topics.AddRange(
                new Topic { Id = TopicId, CourseId = CourseId, Title = "Tema 1", SortOrder = 1 },
                new Topic { Id = OtherCourseTopicId, CourseId = OtherCourseId, Title = "Otro tema", SortOrder = 1 });
            return Task.CompletedTask;
        });
    }

    public async Task DisposeAsync() => await Api.DisposeAsync();

    protected HttpClient As(AppUser user) => Api.ClientFor(user);

    protected static object AssignmentPayload(
        string title = "Ensayo sobre liderazgo",
        long? topicId = TopicId,
        long[]? assignedStudentIds = null,
        string? attachmentUrl = null) => new
    {
        title,
        description = "2-3 páginas",
        dueDate = "2026-11-15T23:59",
        topicId,
        publishAt = "2026-10-01",
        attachmentFileName = attachmentUrl is null ? null : "guia.pdf",
        attachmentUrl,
        attachmentSizeBytes = attachmentUrl is null ? (long?)null : 1024,
        assignedStudentIds = assignedStudentIds ?? Array.Empty<long>(),
    };

    protected async Task<long> CreateAssignmentAsync(long[]? assignedStudentIds = null)
    {
        var response = await As(Teacher).PostAsJsonAsync($"/api/courses/{CourseId}/assignments", AssignmentPayload(assignedStudentIds: assignedStudentIds));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<Dtos.Assignments.AssignmentResponse>();
        return created!.Id;
    }

    private static AppUser User(long id, string role) => new()
    {
        Id = id,
        Name = $"{role} {id}",
        Email = $"{role}{id}@lidessa.test",
        Role = role,
    };
}
