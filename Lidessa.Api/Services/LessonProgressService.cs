using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Progress;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

// Calcado de progressForStudentCourse (avance = tareas/exámenes aprobados) e
// isLessonUnlocked/courseCompletion (lecciones) en LMSContext del frontend.
public class LessonProgressService
{
    private const decimal PassThreshold = 8.0m;

    private readonly AppDbContext _db;

    public LessonProgressService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<long?> GetLessonCourseIdAsync(long lessonId)
    {
        return await _db.Lessons.Where(l => l.Id == lessonId).Select(l => (long?)l.CourseId).SingleOrDefaultAsync();
    }

    public async Task<bool> IsEnrolledAsync(long courseId, long studentId)
    {
        return await _db.CourseEnrollments.AnyAsync(e => e.CourseId == courseId && e.StudentId == studentId);
    }

    // Igual que isLessonUnlocked: si el curso tiene el seguimiento de avance
    // desactivado, no exige orden; si no, cada lección exige que la anterior
    // (por SortOrder, entre las ya publicadas) esté completada. El llamador
    // (controller) ya confirmó que la lección existe y resolvió su courseId.
    public async Task<(CourseProgressResponse Result, string? Error)> MarkLessonCompleteAsync(long lessonId, long courseId, long studentId)
    {
        var trackingEnabled = await _db.Courses.Where(c => c.Id == courseId).Select(c => c.CompletionTrackingEnabled).SingleAsync();

        if (trackingEnabled)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var published = await _db.Lessons
                .Where(l => l.CourseId == courseId && l.PublishAt != null && l.PublishAt <= today)
                .OrderBy(l => l.SortOrder)
                .ToListAsync();

            var idx = published.FindIndex(l => l.Id == lessonId);
            if (idx < 0)
            {
                return (await GetProgressAsync(courseId, studentId), "Esta lección todavía no está publicada");
            }

            if (idx > 0)
            {
                var previousDone = await _db.LessonProgress.AnyAsync(p => p.StudentId == studentId && p.LessonId == published[idx - 1].Id);
                if (!previousDone)
                {
                    return (await GetProgressAsync(courseId, studentId), "Debes completar la lección anterior primero");
                }
            }
        }

        var alreadyDone = await _db.LessonProgress.AnyAsync(p => p.StudentId == studentId && p.LessonId == lessonId);
        if (!alreadyDone)
        {
            _db.LessonProgress.Add(new LessonProgress { StudentId = studentId, CourseId = courseId, LessonId = lessonId, CompletedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync();
        }

        return (await GetProgressAsync(courseId, studentId), null);
    }

    public async Task<CourseProgressResponse> GetProgressAsync(long courseId, long studentId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var completedLessonIds = await _db.LessonProgress
            .Where(p => p.CourseId == courseId && p.StudentId == studentId)
            .Select(p => p.LessonId)
            .ToListAsync();

        var lessonsTotal = await _db.Lessons
            .CountAsync(l => l.CourseId == courseId && l.PublishAt != null && l.PublishAt <= today);

        var (assignmentsDone, assignmentsTotal) = await GetAssignmentsAvanceAsync(courseId, studentId, today);
        var (quizzesDone, quizzesTotal) = await GetQuizzesAvanceAsync(courseId, studentId, today);

        var avanceTotal = assignmentsTotal + quizzesTotal;
        var avanceCompleted = assignmentsDone + quizzesDone;

        return new CourseProgressResponse
        {
            CompletedLessonIds = completedLessonIds,
            LessonsCompleted = completedLessonIds.Count,
            LessonsTotal = lessonsTotal,
            AvanceCompleted = avanceCompleted,
            AvanceTotal = avanceTotal,
            AvancePercent = avanceTotal == 0 ? 0 : (int)Math.Round(avanceCompleted * 100m / avanceTotal),
        };
    }

    // Solo cuenta como avance una tarea aprobada (nota >= 8) — el material de
    // apoyo (lecciones) no cuenta para este porcentaje.
    private async Task<(int Done, int Total)> GetAssignmentsAvanceAsync(long courseId, long studentId, DateOnly today)
    {
        var assignmentIds = await _db.Assignments
            .Where(a => a.CourseId == courseId && a.PublishAt != null && a.PublishAt <= today)
            .Select(a => a.Id)
            .ToListAsync();

        var relevant = await FilterAssignedAsync(assignmentIds, studentId, isQuiz: false);

        var done = await _db.Submissions
            .CountAsync(s => relevant.Contains(s.AssignmentId) && s.StudentId == studentId && s.Status == "graded" && s.Grade >= PassThreshold);

        return (done, relevant.Count);
    }

    private async Task<(int Done, int Total)> GetQuizzesAvanceAsync(long courseId, long studentId, DateOnly today)
    {
        var quizIds = await _db.Quizzes
            .Where(q => q.CourseId == courseId && q.PublishAt != null && q.PublishAt <= today)
            .Select(q => q.Id)
            .ToListAsync();

        var relevant = await FilterAssignedAsync(quizIds, studentId, isQuiz: true);

        var done = await _db.QuizAttempts
            .CountAsync(a => relevant.Contains(a.QuizId) && a.StudentId == studentId && a.Reviewed && a.Score >= PassThreshold);

        return (done, relevant.Count);
    }

    // Una tarea/examen sin asignados puntuales es para todo el curso; si trae
    // lista, solo cuenta para esos estudiantes — igual que isAssignedTo.
    private async Task<List<long>> FilterAssignedAsync(List<long> ids, long studentId, bool isQuiz)
    {
        if (ids.Count == 0)
        {
            return ids;
        }

        var assignees = isQuiz
            ? await _db.QuizAssignees.Where(x => ids.Contains(x.QuizId)).Select(x => new { ParentId = x.QuizId, x.StudentId }).ToListAsync()
            : await _db.AssignmentAssignees.Where(x => ids.Contains(x.AssignmentId)).Select(x => new { ParentId = x.AssignmentId, x.StudentId }).ToListAsync();

        return ids.Where(id =>
        {
            var forThisItem = assignees.Where(a => a.ParentId == id).ToList();
            return forThisItem.Count == 0 || forThisItem.Any(a => a.StudentId == studentId);
        }).ToList();
    }
}
