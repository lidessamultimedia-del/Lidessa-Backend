using System.Text.Json;

namespace Lidessa.Api.Dtos.Services;

public class ServiceResponse
{
    public long Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? HeroImageUrl { get; set; }
    public bool Active { get; set; }
    public bool Locked { get; set; }
    public JsonElement Tabs { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
