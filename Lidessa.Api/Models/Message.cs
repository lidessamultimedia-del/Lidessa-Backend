namespace Lidessa.Api.Models;

public class Message
{
    public long Id { get; set; }
    public string? LegacyId { get; set; }
    public long CourseId { get; set; }
    public long FromUserId { get; set; }
    public long ToUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }

    public Course Course { get; set; } = null!;
    public AppUser FromUser { get; set; } = null!;
    public AppUser ToUser { get; set; } = null!;
}
