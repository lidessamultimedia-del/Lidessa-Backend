using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.QuizAttempts;

public class ReviewQuizAttemptRequest
{
    [Required, Range(0, 10)]
    public decimal Score { get; set; }

    public string? Feedback { get; set; }

    public bool RetryAllowed { get; set; }
}
