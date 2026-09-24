namespace Lidessa.Api.Models;

public class QuizAttemptStart
{
    public long QuizId { get; set; }
    public long StudentId { get; set; }
    public DateTime StartedAt { get; set; }

    public Quiz Quiz { get; set; } = null!;
    public AppUser Student { get; set; } = null!;
}
