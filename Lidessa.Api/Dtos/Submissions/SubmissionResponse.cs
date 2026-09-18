namespace Lidessa.Api.Dtos.Submissions;

public class SubmissionResponse
{
    public long Id { get; set; }
    public long AssignmentId { get; set; }
    public long StudentId { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentUrl { get; set; }
    public long? AttachmentSizeBytes { get; set; }
    public string TextResponse { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? SubmittedAt { get; set; }
    public decimal? Grade { get; set; }
    public string Feedback { get; set; } = string.Empty;
    public DateTime? GradedAt { get; set; }
    public bool RetryAllowed { get; set; }
}
