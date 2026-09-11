using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Topics;

public class TopicRequest
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;
}
