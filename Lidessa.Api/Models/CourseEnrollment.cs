namespace Lidessa.Api.Models;

public class CourseEnrollment
{
    public long CourseId { get; set; }
    public long StudentId { get; set; }
    public DateTime EnrolledAt { get; set; }

    public Course Course { get; set; } = null!;
    public AppUser Student { get; set; } = null!;
}
