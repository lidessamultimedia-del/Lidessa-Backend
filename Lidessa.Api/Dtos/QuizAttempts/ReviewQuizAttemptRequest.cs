using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.QuizAttempts;

public class ReviewQuizAttemptRequest
{
    // Range(0, 10) con enteros convierte el decimal a int antes de comparar
    // (10.4 → 10 pasaba); con typeof(decimal) se valida el valor real.
    [Required, Range(typeof(decimal), "0", "10", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Score { get; set; }

    public string? Feedback { get; set; }

    public bool RetryAllowed { get; set; }
}
