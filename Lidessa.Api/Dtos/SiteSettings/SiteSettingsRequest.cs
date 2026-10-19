using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.SiteSettings;

public class SiteSettingsRequest
{
    [Required, MaxLength(30)]
    public string Phone { get; set; } = string.Empty;

    [Required, MaxLength(256), EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Address { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Schedule { get; set; } = string.Empty;
}
