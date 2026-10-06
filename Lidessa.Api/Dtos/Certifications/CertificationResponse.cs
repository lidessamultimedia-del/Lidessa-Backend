namespace Lidessa.Api.Dtos.Certifications;

public class CertificationResponse
{
    public long Id { get; set; }
    public long StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentEmail { get; set; } = string.Empty;
    public long CourseId { get; set; }
    public string CourseName { get; set; } = string.Empty;
    public DateTime MarkedAt { get; set; }
}
