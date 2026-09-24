using System.Text.Json;

namespace Lidessa.Api.Dtos.QuizAttempts;

public class QuizAttemptResponse
{
    public long Id { get; set; }
    public long QuizId { get; set; }
    public long StudentId { get; set; }
    public List<JsonElement> Answers { get; set; } = new();
    public decimal Score { get; set; }
    public string Feedback { get; set; } = string.Empty;
    public bool Reviewed { get; set; }
    public bool RetryAllowed { get; set; }
    public bool Seen { get; set; }
    public DateTime SubmittedAt { get; set; }
}
