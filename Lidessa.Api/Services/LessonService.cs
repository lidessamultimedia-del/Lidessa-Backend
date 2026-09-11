using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Lessons;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class LessonService
{
    private readonly AppDbContext _db;

    public LessonService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<long?> GetLessonCourseIdAsync(long lessonId)
    {
        return await _db.Lessons.Where(l => l.Id == lessonId).Select(l => (long?)l.CourseId).SingleOrDefaultAsync();
    }

    public async Task<List<LessonResponse>> GetAllByCourseAsync(long courseId)
    {
        return await _db.Lessons
            .Where(l => l.CourseId == courseId)
            .OrderBy(l => l.SortOrder)
            .Select(l => ToResponse(l))
            .ToListAsync();
    }

    public async Task<LessonResponse?> GetByIdAsync(long id)
    {
        var entity = await _db.Lessons.FindAsync(id);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<(LessonResponse? Result, string? Error)> CreateAsync(long courseId, LessonRequest request)
    {
        if (request.TopicId is not null)
        {
            var topicBelongs = await _db.Topics.AnyAsync(t => t.Id == request.TopicId && t.CourseId == courseId);
            if (!topicBelongs)
            {
                return (null, "El tema indicado no pertenece a este curso");
            }
        }

        var nextOrder = await _db.Lessons.CountAsync(l => l.CourseId == courseId) + 1;

        var entity = new Lesson
        {
            CourseId = courseId,
            TopicId = request.TopicId,
            Title = request.Title.Trim(),
            Content = request.Content ?? string.Empty,
            SortOrder = nextOrder,
            PublishAt = request.PublishAt,
            AttachmentFileName = request.AttachmentFileName,
            AttachmentUrl = request.AttachmentUrl,
            AttachmentSizeBytes = request.AttachmentSizeBytes,
        };

        _db.Lessons.Add(entity);
        await _db.SaveChangesAsync();

        return (ToResponse(entity), null);
    }

    public async Task<(LessonResponse? Result, string? Error)> UpdateAsync(long id, LessonRequest request)
    {
        var entity = await _db.Lessons.FindAsync(id);
        if (entity is null)
        {
            return (null, "Lección no encontrada");
        }

        if (request.TopicId is not null)
        {
            var topicBelongs = await _db.Topics.AnyAsync(t => t.Id == request.TopicId && t.CourseId == entity.CourseId);
            if (!topicBelongs)
            {
                return (null, "El tema indicado no pertenece a este curso");
            }
        }

        entity.TopicId = request.TopicId;
        entity.Title = request.Title.Trim();
        entity.Content = request.Content ?? string.Empty;
        entity.PublishAt = request.PublishAt;
        entity.AttachmentFileName = request.AttachmentFileName;
        entity.AttachmentUrl = request.AttachmentUrl;
        entity.AttachmentSizeBytes = request.AttachmentSizeBytes;

        await _db.SaveChangesAsync();

        return (ToResponse(entity), null);
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var entity = await _db.Lessons.FindAsync(id);
        if (entity is null)
        {
            return false;
        }

        _db.Lessons.Remove(entity);
        await _db.SaveChangesAsync();

        return true;
    }

    private static LessonResponse ToResponse(Lesson l) => new()
    {
        Id = l.Id,
        CourseId = l.CourseId,
        TopicId = l.TopicId,
        Title = l.Title,
        Content = l.Content,
        SortOrder = l.SortOrder,
        PublishAt = l.PublishAt,
        AttachmentFileName = l.AttachmentFileName,
        AttachmentUrl = l.AttachmentUrl,
        AttachmentSizeBytes = l.AttachmentSizeBytes,
    };
}
