namespace Lidessa.Api.Models;

public class Certification
{
    public long Id { get; set; }
    public string? LegacyId { get; set; }
    public long StudentId { get; set; }
    public long CourseId { get; set; }
    public DateTime MarkedAt { get; set; }

    public AppUser Student { get; set; } = null!;
    public Course Course { get; set; } = null!;
}
