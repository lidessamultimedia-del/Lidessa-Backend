namespace Lidessa.Api.Dtos.Files;

public class AttachmentUploadResponse
{
    public string FileName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
