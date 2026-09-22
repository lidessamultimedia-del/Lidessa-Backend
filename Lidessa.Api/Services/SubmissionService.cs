using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Submissions;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class SubmissionService
{
    private readonly AppDbContext _db;

    public SubmissionService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<long?> GetAssignmentCourseIdAsync(long assignmentId)
    {
        return await _db.Assignments.Where(a => a.Id == assignmentId).Select(a => (long?)a.CourseId).SingleOrDefaultAsync();
    }

    public async Task<long?> GetSubmissionCourseIdAsync(long submissionId)
    {
        return await _db.Submissions
            .Where(s => s.Id == submissionId)
            .Select(s => (long?)s.Assignment.CourseId)
            .SingleOrDefaultAsync();
    }

    public async Task<bool> IsAssignedAsync(long assignmentId, long courseId, long studentId)
    {
        var enrolled = await _db.CourseEnrollments.AnyAsync(e => e.CourseId == courseId && e.StudentId == studentId);
        if (!enrolled)
        {
            return false;
        }

        var hasSpecificAssignees = await _db.AssignmentAssignees.AnyAsync(a => a.AssignmentId == assignmentId);
        return !hasSpecificAssignees || await _db.AssignmentAssignees.AnyAsync(a => a.AssignmentId == assignmentId && a.StudentId == studentId);
    }

    public async Task<SubmissionResponse?> GetMineAsync(long assignmentId, long studentId)
    {
        var entity = await _db.Submissions.SingleOrDefaultAsync(s => s.AssignmentId == assignmentId && s.StudentId == studentId);
        return entity is null ? null : ToResponse(entity);
    }

    // Máquina de estados de la entrega: sin entrega -> draft -> submitted.
    // "submitted" es de un solo sentido (ya no se puede volver a guardar como
    // borrador) y "graded" queda bloqueada salvo que el profesor autorice un
    // reintento, que la reabre como si fuera un intento nuevo.
    public async Task<(SubmissionResponse? Result, string? Error)> SaveAsync(long assignmentId, long studentId, SubmissionRequest request)
    {
        var entity = await _db.Submissions.SingleOrDefaultAsync(s => s.AssignmentId == assignmentId && s.StudentId == studentId);

        if (entity is not null)
        {
            var error = ValidateTransition(entity, request.Submit);
            if (error is not null)
            {
                return (null, error);
            }
        }

        var reopeningAfterGrade = entity is not null && entity.Status == "graded" && entity.RetryAllowed;

        if (entity is null)
        {
            entity = new Submission { AssignmentId = assignmentId, StudentId = studentId };
            _db.Submissions.Add(entity);
        }

        entity.TextResponse = request.TextResponse ?? string.Empty;
        entity.Notes = request.Notes ?? string.Empty;
        entity.AttachmentFileName = request.AttachmentFileName;
        entity.AttachmentUrl = request.AttachmentUrl;
        entity.AttachmentSizeBytes = request.AttachmentSizeBytes;

        if (reopeningAfterGrade)
        {
            entity.Grade = null;
            entity.Feedback = string.Empty;
            entity.GradedAt = null;
            entity.Seen = false;
        }

        if (request.Submit)
        {
            entity.Status = "submitted";
            entity.SubmittedAt = DateTime.UtcNow;
            entity.RetryAllowed = false;
        }
        else
        {
            entity.Status = "draft";
        }

        await _db.SaveChangesAsync();

        return (ToResponse(entity), null);
    }

    // Calificar: solo aplica sobre una entrega ya "submitted" (calcado de como
    // TeacherDashboard/CourseGradingQueue solo ofrecen calificar lo que viene
    // de submissionsPendingForTeacher, que filtra por status === 'submitted').
    public async Task<(SubmissionResponse? Result, string? Error)> GradeAsync(long submissionId, GradeSubmissionRequest request)
    {
        var entity = await _db.Submissions.FindAsync(submissionId);
        if (entity is null)
        {
            return (null, "Entrega no encontrada");
        }

        if (entity.Status != "submitted")
        {
            return (null, "Solo se puede calificar una entrega que ya fue entregada");
        }

        entity.Grade = request.Grade;
        entity.Feedback = request.Feedback ?? string.Empty;
        entity.Status = "graded";
        entity.GradedAt = DateTime.UtcNow;
        entity.RetryAllowed = request.RetryAllowed;
        entity.Seen = false;

        await _db.SaveChangesAsync();

        return (ToResponse(entity), null);
    }

    // El estudiante marca como "visto" el resultado (calificación/feedback)
    // de su propia entrega. Filtrar por studentId hace de chequeo de
    // pertenencia, igual que GetMineAsync.
    public async Task<SubmissionResponse?> MarkSeenAsync(long submissionId, long studentId)
    {
        var entity = await _db.Submissions.SingleOrDefaultAsync(s => s.Id == submissionId && s.StudentId == studentId);
        if (entity is null)
        {
            return null;
        }

        if (!entity.Seen)
        {
            entity.Seen = true;
            await _db.SaveChangesAsync();
        }

        return ToResponse(entity);
    }

    private static string? ValidateTransition(Submission entity, bool submit)
    {
        if (entity.Status == "graded" && !entity.RetryAllowed)
        {
            return "Esta entrega ya fue calificada. Pide a tu profesor que habilite un reintento.";
        }

        if (entity.Status == "submitted" && !submit)
        {
            return "Ya entregaste esta tarea; no puedes volver a guardarla como borrador.";
        }

        return null;
    }

    private static SubmissionResponse ToResponse(Submission s) => new()
    {
        Id = s.Id,
        AssignmentId = s.AssignmentId,
        StudentId = s.StudentId,
        AttachmentFileName = s.AttachmentFileName,
        AttachmentUrl = s.AttachmentUrl,
        AttachmentSizeBytes = s.AttachmentSizeBytes,
        TextResponse = s.TextResponse,
        Notes = s.Notes,
        Status = s.Status,
        SubmittedAt = s.SubmittedAt,
        Grade = s.Grade,
        Feedback = s.Feedback,
        GradedAt = s.GradedAt,
        RetryAllowed = s.RetryAllowed,
        Seen = s.Seen,
    };
}
