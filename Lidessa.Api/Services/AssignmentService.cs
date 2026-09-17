using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Assignments;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class AssignmentService
{
    private const decimal DefaultMaxScore = 10.0m;

    private readonly AppDbContext _db;

    public AssignmentService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<long?> GetAssignmentCourseIdAsync(long assignmentId)
    {
        return await _db.Assignments.Where(a => a.Id == assignmentId).Select(a => (long?)a.CourseId).SingleOrDefaultAsync();
    }

    public async Task<List<AssignmentResponse>> GetAllByCourseAsync(long courseId)
    {
        var assignments = await _db.Assignments
            .Where(a => a.CourseId == courseId)
            .OrderBy(a => a.DueDate)
            .ToListAsync();

        var assigneesByAssignment = await _db.AssignmentAssignees
            .Where(x => assignments.Select(a => a.Id).Contains(x.AssignmentId))
            .ToListAsync();

        return assignments.Select(a => ToResponse(a, assigneesByAssignment)).ToList();
    }

    public async Task<AssignmentResponse?> GetByIdAsync(long id)
    {
        var entity = await _db.Assignments.FindAsync(id);
        if (entity is null)
        {
            return null;
        }

        var assignees = await _db.AssignmentAssignees.Where(x => x.AssignmentId == id).ToListAsync();
        return ToResponse(entity, assignees);
    }

    public async Task<(AssignmentResponse? Result, string? Error)> CreateAsync(long courseId, AssignmentRequest request)
    {
        var error = await ValidateAsync(courseId, request);
        if (error is not null)
        {
            return (null, error);
        }

        var entity = new Assignment
        {
            CourseId = courseId,
            TopicId = request.TopicId,
            Title = request.Title.Trim(),
            Description = request.Description ?? string.Empty,
            DueDate = request.DueDate,
            MaxScore = DefaultMaxScore,
            PublishAt = request.PublishAt,
            AttachmentFileName = request.AttachmentFileName,
            AttachmentUrl = request.AttachmentUrl,
            AttachmentSizeBytes = request.AttachmentSizeBytes,
        };

        _db.Assignments.Add(entity);
        await _db.SaveChangesAsync();

        await SyncAssigneesAsync(entity.Id, request.AssignedStudentIds);

        return (await GetByIdAsync(entity.Id), null);
    }

    public async Task<(AssignmentResponse? Result, string? Error)> UpdateAsync(long id, AssignmentRequest request)
    {
        var entity = await _db.Assignments.FindAsync(id);
        if (entity is null)
        {
            return (null, "Tarea no encontrada");
        }

        var error = await ValidateAsync(entity.CourseId, request);
        if (error is not null)
        {
            return (null, error);
        }

        entity.TopicId = request.TopicId;
        entity.Title = request.Title.Trim();
        entity.Description = request.Description ?? string.Empty;
        entity.DueDate = request.DueDate;
        entity.PublishAt = request.PublishAt;
        entity.AttachmentFileName = request.AttachmentFileName;
        entity.AttachmentUrl = request.AttachmentUrl;
        entity.AttachmentSizeBytes = request.AttachmentSizeBytes;

        await _db.SaveChangesAsync();
        await SyncAssigneesAsync(id, request.AssignedStudentIds);

        return (await GetByIdAsync(id), null);
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var entity = await _db.Assignments.FindAsync(id);
        if (entity is null)
        {
            return false;
        }

        // Las entregas (Submission) se borran solas por el ON DELETE CASCADE
        // del schema — igual que hace LMSContext.deleteAssignment en el frontend.
        _db.Assignments.Remove(entity);
        await _db.SaveChangesAsync();

        return true;
    }

    private async Task SyncAssigneesAsync(long assignmentId, List<long> studentIds)
    {
        var current = await _db.AssignmentAssignees.Where(x => x.AssignmentId == assignmentId).ToListAsync();
        _db.AssignmentAssignees.RemoveRange(current);

        foreach (var studentId in studentIds.Distinct())
        {
            _db.AssignmentAssignees.Add(new AssignmentAssignee { AssignmentId = assignmentId, StudentId = studentId });
        }

        await _db.SaveChangesAsync();
    }

    private async Task<string?> ValidateAsync(long courseId, AssignmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "El título de la tarea es obligatorio";
        }

        if (request.TopicId is not null)
        {
            var topicBelongs = await _db.Topics.AnyAsync(t => t.Id == request.TopicId && t.CourseId == courseId);
            if (!topicBelongs)
            {
                return "El tema indicado no pertenece a este curso";
            }
        }

        if (request.AssignedStudentIds.Count > 0)
        {
            var enrolledCount = await _db.CourseEnrollments
                .CountAsync(e => e.CourseId == courseId && request.AssignedStudentIds.Contains(e.StudentId));

            if (enrolledCount != request.AssignedStudentIds.Distinct().Count())
            {
                return "Uno o más estudiantes asignados no están inscritos en este curso";
            }
        }

        return null;
    }

    private static AssignmentResponse ToResponse(Assignment a, List<AssignmentAssignee> allAssignees) => new()
    {
        Id = a.Id,
        CourseId = a.CourseId,
        TopicId = a.TopicId,
        Title = a.Title,
        Description = a.Description,
        DueDate = a.DueDate,
        MaxScore = a.MaxScore,
        PublishAt = a.PublishAt,
        AttachmentFileName = a.AttachmentFileName,
        AttachmentUrl = a.AttachmentUrl,
        AttachmentSizeBytes = a.AttachmentSizeBytes,
        AssignedStudentIds = allAssignees.Where(x => x.AssignmentId == a.Id).Select(x => x.StudentId).ToList(),
    };
}
