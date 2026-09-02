namespace Lidessa.Api.Models;

public class AssignmentAssignee
{
    public long AssignmentId { get; set; }
    public long StudentId { get; set; }

    public Assignment Assignment { get; set; } = null!;
    public AppUser Student { get; set; } = null!;
}
