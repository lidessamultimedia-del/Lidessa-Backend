using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Messages;

public class SendMessageRequest
{
    [Required]
    public long ToUserId { get; set; }

    [Required]
    public string Body { get; set; } = string.Empty;
}
