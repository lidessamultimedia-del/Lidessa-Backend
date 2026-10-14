using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Services;

public class ServiceCategoryRequest
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; } = true;
}
