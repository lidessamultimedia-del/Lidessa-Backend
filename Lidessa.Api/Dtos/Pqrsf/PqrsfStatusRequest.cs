using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Pqrsf;

public class PqrsfStatusRequest
{
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = string.Empty;
}
