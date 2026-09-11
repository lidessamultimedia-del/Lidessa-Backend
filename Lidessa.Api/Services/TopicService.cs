using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Topics;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class TopicService
{
    private readonly AppDbContext _db;

    public TopicService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<long?> GetCourseTeacherIdAsync(long courseId)
    {
        return await _db.Courses.Where(c => c.Id == courseId).Select(c => c.TeacherId).SingleOrDefaultAsync();
    }

    public async Task<bool> CourseExistsAsync(long courseId)
    {
        return await _db.Courses.AnyAsync(c => c.Id == courseId);
    }

    public async Task<long?> GetTopicCourseIdAsync(long topicId)
    {
        var courseId = await _db.Topics.Where(t => t.Id == topicId).Select(t => (long?)t.CourseId).SingleOrDefaultAsync();
        return courseId;
    }

    public async Task<List<TopicResponse>> GetAllByCourseAsync(long courseId)
    {
        return await _db.Topics
            .Where(t => t.CourseId == courseId)
            .OrderBy(t => t.SortOrder)
            .Select(t => ToResponse(t))
            .ToListAsync();
    }

    public async Task<TopicResponse?> GetByIdAsync(long id)
    {
        var entity = await _db.Topics.FindAsync(id);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<TopicResponse> CreateAsync(long courseId, TopicRequest request)
    {
        var nextOrder = await _db.Topics.CountAsync(t => t.CourseId == courseId) + 1;

        var entity = new Topic
        {
            CourseId = courseId,
            Title = request.Title.Trim(),
            SortOrder = nextOrder,
        };

        _db.Topics.Add(entity);
        await _db.SaveChangesAsync();

        return ToResponse(entity);
    }

    public async Task<(TopicResponse? Result, string? Error)> UpdateAsync(long id, TopicRequest request)
    {
        var entity = await _db.Topics.FindAsync(id);
        if (entity is null)
        {
            return (null, "Tema no encontrado");
        }

        entity.Title = request.Title.Trim();
        await _db.SaveChangesAsync();

        return (ToResponse(entity), null);
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var entity = await _db.Topics.FindAsync(id);
        if (entity is null)
        {
            return false;
        }

        // El tema se puede borrar aunque tenga contenido: las lecciones,
        // tareas y examenes que lo tenian asignado quedan sin tema en vez
        // de borrarse (igual que hoy hace LMSContext.deleteTopic).
        await _db.Lessons.Where(l => l.TopicId == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.TopicId, (long?)null));
        await _db.Assignments.Where(a => a.TopicId == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.TopicId, (long?)null));
        await _db.Quizzes.Where(q => q.TopicId == id).ExecuteUpdateAsync(s => s.SetProperty(q => q.TopicId, (long?)null));

        _db.Topics.Remove(entity);
        await _db.SaveChangesAsync();

        return true;
    }

    private static TopicResponse ToResponse(Topic t) => new()
    {
        Id = t.Id,
        CourseId = t.CourseId,
        Title = t.Title,
        SortOrder = t.SortOrder,
    };
}
