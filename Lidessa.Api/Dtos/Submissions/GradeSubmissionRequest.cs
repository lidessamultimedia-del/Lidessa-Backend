using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Submissions;

public class GradeSubmissionRequest
{
    [Required, Range(0, 10)]
    public decimal Grade { get; set; }

    public string? Feedback { get; set; }

    public bool RetryAllowed { get; set; }
}
