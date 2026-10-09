namespace Lidessa.Api.Dtos.BlogPosts;

public class BlogPostResponse
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public DateOnly PublishedOn { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? ExternalLink { get; set; }
    public long? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
