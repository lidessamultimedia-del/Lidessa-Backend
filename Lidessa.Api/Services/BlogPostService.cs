using Lidessa.Api.Data;
using Lidessa.Api.Dtos.BlogPosts;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class BlogPostService
{
    private readonly AppDbContext _db;

    public BlogPostService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<BlogPostResponse>> GetAllAsync()
    {
        return await _db.BlogPosts
            .OrderByDescending(b => b.PublishedOn)
            .ThenByDescending(b => b.Id)
            .Select(b => ToResponse(b))
            .ToListAsync();
    }

    public async Task<BlogPostResponse?> GetByIdAsync(long id)
    {
        var entity = await _db.BlogPosts.FindAsync(id);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<BlogPostResponse> CreateAsync(BlogPostRequest request, long? createdByUserId)
    {
        var entity = new BlogPost
        {
            Title = request.Title,
            Excerpt = request.Excerpt,
            PublishedOn = request.PublishedOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
            ImageUrl = request.ImageUrl,
            Author = request.Author,
            Phone = request.Phone ?? string.Empty,
            ExternalLink = request.ExternalLink,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
        };
        _db.BlogPosts.Add(entity);
        await _db.SaveChangesAsync();

        return ToResponse(entity);
    }

    public async Task<BlogPostResponse?> UpdateAsync(long id, BlogPostRequest request)
    {
        var entity = await _db.BlogPosts.FindAsync(id);
        if (entity is null)
        {
            return null;
        }

        entity.Title = request.Title;
        entity.Excerpt = request.Excerpt;
        entity.PublishedOn = request.PublishedOn ?? entity.PublishedOn;
        entity.ImageUrl = request.ImageUrl;
        entity.Author = request.Author;
        entity.Phone = request.Phone ?? string.Empty;
        entity.ExternalLink = request.ExternalLink;
        await _db.SaveChangesAsync();

        return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var entity = await _db.BlogPosts.FindAsync(id);
        if (entity is null)
        {
            return false;
        }

        _db.BlogPosts.Remove(entity);
        await _db.SaveChangesAsync();

        return true;
    }

    private static BlogPostResponse ToResponse(BlogPost b) => new()
    {
        Id = b.Id,
        Title = b.Title,
        Excerpt = b.Excerpt,
        PublishedOn = b.PublishedOn,
        ImageUrl = b.ImageUrl,
        Author = b.Author,
        Phone = b.Phone,
        ExternalLink = b.ExternalLink,
        CreatedByUserId = b.CreatedByUserId,
        CreatedAt = b.CreatedAt,
    };
}
