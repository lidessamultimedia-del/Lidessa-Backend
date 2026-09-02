namespace Lidessa.Api.Models;

public class QuizAttempt
{
    public long Id { get; set; }
    public string? LegacyId { get; set; }
    public long QuizId { get; set; }
    public long StudentId { get; set; }
    public string AnswersJson { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public string Feedback { get; set; } = string.Empty;
    public bool Reviewed { get; set; }
    public bool RetryAllowed { get; set; }
    public bool Seen { get; set; }
    public DateTime SubmittedAt { get; set; }

    public Quiz Quiz { get; set; } = null!;
    public AppUser Student { get; set; } = null!;
}
