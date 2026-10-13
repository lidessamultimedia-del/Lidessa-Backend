using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Pqrsf;

public class PqrsfRespondRequest
{
    [Required]
    public string Response { get; set; } = string.Empty;
}
