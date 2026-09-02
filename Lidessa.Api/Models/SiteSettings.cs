namespace Lidessa.Api.Models;

public class SiteSettings
{
    public int Id { get; set; } = 1;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Schedule { get; set; } = string.Empty;
}
