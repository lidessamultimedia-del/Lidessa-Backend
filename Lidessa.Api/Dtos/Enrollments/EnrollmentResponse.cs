namespace Lidessa.Api.Dtos.Enrollments;

public class EnrollmentResponse
{
    public long CourseId { get; set; }
    public long StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentEmail { get; set; } = string.Empty;
    public DateTime EnrolledAt { get; set; }
}
