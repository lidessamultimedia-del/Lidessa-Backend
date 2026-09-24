using System.Text.Json;
using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Quizzes;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class QuizService
{
    private const string TypeMultiple = "multiple";
    private const string TypeOpen = "open";

    private readonly AppDbContext _db;

    public QuizService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<long?> GetQuizCourseIdAsync(long quizId)
    {
        return await _db.Quizzes.Where(q => q.Id == quizId).Select(q => (long?)q.CourseId).SingleOrDefaultAsync();
    }

    public async Task<List<QuizResponse>> GetAllByCourseAsync(long courseId, bool includeAnswers)
    {
        var quizzes = await _db.Quizzes
            .Where(q => q.CourseId == courseId)
            .OrderBy(q => q.SortOrder)
            .ToListAsync();

        var quizIds = quizzes.Select(q => q.Id).ToList();

        var questions = await _db.QuizQuestions
            .Where(x => quizIds.Contains(x.QuizId))
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        var assignees = await _db.QuizAssignees
            .Where(x => quizIds.Contains(x.QuizId))
            .ToListAsync();

        return quizzes.Select(q => ToResponse(q, questions, assignees, includeAnswers)).ToList();
    }

    public async Task<QuizResponse?> GetByIdAsync(long id, bool includeAnswers)
    {
        var entity = await _db.Quizzes.FindAsync(id);
        if (entity is null)
        {
            return null;
        }

        var questions = await _db.QuizQuestions.Where(x => x.QuizId == id).OrderBy(x => x.SortOrder).ToListAsync();
        var assignees = await _db.QuizAssignees.Where(x => x.QuizId == id).ToListAsync();
        return ToResponse(entity, questions, assignees, includeAnswers);
    }

    public async Task<(QuizResponse? Result, string? Error)> CreateAsync(long courseId, QuizRequest request)
    {
        var error = await ValidateAsync(courseId, request);
        if (error is not null)
        {
            return (null, error);
        }

        var nextOrder = await _db.Quizzes.CountAsync(q => q.CourseId == courseId) + 1;

        var entity = new Quiz
        {
            CourseId = courseId,
            TopicId = request.TopicId,
            Title = request.Title.Trim(),
            Description = request.Description ?? string.Empty,
            DueDate = request.DueDate,
            PublishAt = request.PublishAt,
            TimeLimitMinutes = request.TimeLimitMinutes,
            SortOrder = nextOrder,
        };

        _db.Quizzes.Add(entity);
        await _db.SaveChangesAsync();

        await SyncQuestionsAsync(entity.Id, request.Questions);
        await SyncAssigneesAsync(entity.Id, request.AssignedStudentIds);

        return (await GetByIdAsync(entity.Id, includeAnswers: true), null);
    }

    public async Task<(QuizResponse? Result, string? Error)> UpdateAsync(long id, QuizRequest request)
    {
        var entity = await _db.Quizzes.FindAsync(id);
        if (entity is null)
        {
            return (null, "Examen no encontrado");
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
        entity.TimeLimitMinutes = request.TimeLimitMinutes;

        await _db.SaveChangesAsync();
        await SyncQuestionsAsync(id, request.Questions);
        await SyncAssigneesAsync(id, request.AssignedStudentIds);

        return (await GetByIdAsync(id, includeAnswers: true), null);
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var entity = await _db.Quizzes.FindAsync(id);
        if (entity is null)
        {
            return false;
        }

        // Preguntas, asignados e intentos se borran solos por el ON DELETE
        // CASCADE del schema — igual que hace LMSContext.deleteQuiz en el frontend.
        _db.Quizzes.Remove(entity);
        await _db.SaveChangesAsync();

        return true;
    }

    // Las preguntas se reemplazan completas: el frontend no conserva ids de
    // pregunta entre ediciones y los intentos (QuizAttempt.AnswersJson) se
    // alinean por posición, no por id.
    private async Task SyncQuestionsAsync(long quizId, List<QuizQuestionRequest> questions)
    {
        var current = await _db.QuizQuestions.Where(x => x.QuizId == quizId).ToListAsync();
        _db.QuizQuestions.RemoveRange(current);

        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var isOpen = q.Type == TypeOpen;

            _db.QuizQuestions.Add(new QuizQuestion
            {
                QuizId = quizId,
                SortOrder = i,
                QuestionType = q.Type,
                QuestionText = q.Text.Trim(),
                OptionsJson = isOpen ? null : JsonSerializer.Serialize(q.Options!.Select(o => o.Trim())),
                CorrectIndex = isOpen ? null : q.CorrectIndex,
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task SyncAssigneesAsync(long quizId, List<long> studentIds)
    {
        var current = await _db.QuizAssignees.Where(x => x.QuizId == quizId).ToListAsync();
        _db.QuizAssignees.RemoveRange(current);

        foreach (var studentId in studentIds.Distinct())
        {
            _db.QuizAssignees.Add(new QuizAssignee { QuizId = quizId, StudentId = studentId });
        }

        await _db.SaveChangesAsync();
    }

    private async Task<string?> ValidateAsync(long courseId, QuizRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "El título del examen es obligatorio";
        }

        var questionError = ValidateQuestions(request.Questions);
        if (questionError is not null)
        {
            return questionError;
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

    private static string? ValidateQuestions(List<QuizQuestionRequest> questions)
    {
        if (questions.Count == 0)
        {
            return "Agregue al menos una pregunta";
        }

        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var label = $"Pregunta {i + 1}";

            if (q.Type != TypeMultiple && q.Type != TypeOpen)
            {
                return $"{label}: el tipo debe ser 'multiple' u 'open'";
            }

            if (string.IsNullOrWhiteSpace(q.Text))
            {
                return $"{label}: el texto es obligatorio";
            }

            if (q.Type == TypeOpen)
            {
                continue;
            }

            if (q.Options is null || q.Options.Count < 2)
            {
                return $"{label}: una pregunta de selección múltiple necesita al menos 2 opciones";
            }

            if (q.Options.Any(string.IsNullOrWhiteSpace))
            {
                return $"{label}: todas las opciones deben tener texto";
            }

            if (q.CorrectIndex is null || q.CorrectIndex < 0 || q.CorrectIndex >= q.Options.Count)
            {
                return $"{label}: indique cuál es la opción correcta";
            }
        }

        return null;
    }

    private static QuizResponse ToResponse(
        Quiz q, List<QuizQuestion> allQuestions, List<QuizAssignee> allAssignees, bool includeAnswers) => new()
    {
        Id = q.Id,
        CourseId = q.CourseId,
        TopicId = q.TopicId,
        Title = q.Title,
        Description = q.Description,
        DueDate = q.DueDate,
        PublishAt = q.PublishAt,
        TimeLimitMinutes = q.TimeLimitMinutes,
        SortOrder = q.SortOrder,
        AssignedStudentIds = allAssignees.Where(x => x.QuizId == q.Id).Select(x => x.StudentId).ToList(),
        Questions = allQuestions
            .Where(x => x.QuizId == q.Id)
            .Select(x => new QuizQuestionResponse
            {
                Id = x.Id,
                SortOrder = x.SortOrder,
                Type = x.QuestionType,
                Text = x.QuestionText,
                Options = x.OptionsJson is null ? new List<string>() : JsonSerializer.Deserialize<List<string>>(x.OptionsJson) ?? new List<string>(),
                CorrectIndex = includeAnswers ? x.CorrectIndex : null,
            })
            .ToList(),
    };
}
