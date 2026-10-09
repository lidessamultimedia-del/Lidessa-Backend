using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.BlogPosts;

public class BlogPostRequest
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string Excerpt { get; set; } = string.Empty;

    public DateOnly? PublishedOn { get; set; }

    [Required]
    [MaxLength(500)]
    public string ImageUrl { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string Author { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(500)]
    public string? ExternalLink { get; set; }
}
