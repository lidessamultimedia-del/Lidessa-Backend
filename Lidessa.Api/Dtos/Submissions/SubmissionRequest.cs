using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Submissions;

public class SubmissionRequest
{
    public string? TextResponse { get; set; }

    public string? Notes { get; set; }

    [MaxLength(260)]
    public string? AttachmentFileName { get; set; }

    [MaxLength(500)]
    public string? AttachmentUrl { get; set; }

    public long? AttachmentSizeBytes { get; set; }

    // false = guardar como borrador; true = entregar (draft -> submitted).
    public bool Submit { get; set; }
}
