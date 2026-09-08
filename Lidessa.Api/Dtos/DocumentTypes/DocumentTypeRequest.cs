using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.DocumentTypes;

public class DocumentTypeRequest
{
    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;
}
