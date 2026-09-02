namespace Lidessa.Api.Models;

public class Service
{
    public long Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public long CategoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? HeroImageUrl { get; set; }
    public bool Active { get; set; } = true;
    public bool Locked { get; set; }
    public string TabsJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ServiceCategory Category { get; set; } = null!;
}
