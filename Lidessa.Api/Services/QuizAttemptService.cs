using System.Text.Json;
using Lidessa.Api.Data;
using Lidessa.Api.Dtos.QuizAttempts;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class QuizAttemptService
{
    // Misma escala que el frontend (MAX_GRADE / PASS_THRESHOLD en LMSContext).
    private const decimal MaxScore = 10.0m;
    private const decimal PassThreshold = 8.0m;

    // Margen para la latencia de red: el navegador entrega solo cuando el
    // cronómetro llega a cero, y esa petición tarda algo en llegar.
    private static readonly TimeSpan SubmitGrace = TimeSpan.FromSeconds(30);

    private readonly AppDbContext _db;

    public QuizAttemptService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<long?> GetQuizCourseIdAsync(long quizId)
    {
        return await _db.Quizzes.Where(q => q.Id == quizId).Select(q => (long?)q.CourseId).SingleOrDefaultAsync();
    }

    public async Task<long?> GetAttemptCourseIdAsync(long attemptId)
    {
        return await _db.QuizAttempts
            .Where(a => a.Id == attemptId)
            .Select(a => (long?)a.Quiz.CourseId)
            .SingleOrDefaultAsync();
    }

    public async Task<bool> IsAssignedAsync(long quizId, long courseId, long studentId)
    {
        var enrolled = await _db.CourseEnrollments.AnyAsync(e => e.CourseId == courseId && e.StudentId == studentId);
        if (!enrolled)
        {
            return false;
        }

        var hasSpecificAssignees = await _db.QuizAssignees.AnyAsync(a => a.QuizId == quizId);
        return !hasSpecificAssignees || await _db.QuizAssignees.AnyAsync(a => a.QuizId == quizId && a.StudentId == studentId);
    }

    public async Task<QuizAttemptResponse?> GetMineAsync(long quizId, long studentId)
    {
        var entity = await _db.QuizAttempts.SingleOrDefaultAsync(a => a.QuizId == quizId && a.StudentId == studentId);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<List<QuizAttemptResponse>> GetAllByQuizAsync(long quizId)
    {
        var attempts = await _db.QuizAttempts
            .Where(a => a.QuizId == quizId)
            .OrderBy(a => a.SubmittedAt)
            .ToListAsync();

        return attempts.Select(ToResponse).ToList();
    }

    // Abrir el examen: registra cuándo empezó el estudiante. Si ya lo había
    // abierto (ej. recargó la página) devuelve el inicio original, así el
    // tiempo no se reinicia.
    public async Task<(QuizAttemptStartResponse? Result, string? Error)> StartAsync(long quizId, long studentId)
    {
        var existing = await _db.QuizAttempts.SingleOrDefaultAsync(a => a.QuizId == quizId && a.StudentId == studentId);
        if (existing is not null)
        {
            var retryError = ValidateRetry(existing);
            if (retryError is not null)
            {
                return (null, retryError);
            }
        }

        var timeLimit = await _db.Quizzes.Where(q => q.Id == quizId).Select(q => q.TimeLimitMinutes).SingleAsync();

        var start = await _db.QuizAttemptStarts.FindAsync(quizId, studentId);
        if (start is null)
        {
            start = new QuizAttemptStart { QuizId = quizId, StudentId = studentId, StartedAt = DateTime.UtcNow };
            _db.QuizAttemptStarts.Add(start);
            await _db.SaveChangesAsync();
        }

        int? remaining = null;
        if (timeLimit is not null)
        {
            var deadline = start.StartedAt.AddMinutes(timeLimit.Value);
            remaining = Math.Max(0, (int)Math.Floor((deadline - DateTime.UtcNow).TotalSeconds));
        }

        return (new QuizAttemptStartResponse { StartedAt = AsUtc(start.StartedAt), TimeLimitMinutes = timeLimit, RemainingSeconds = remaining }, null);
    }

    // Entregar el examen: se califica aquí (el estudiante nunca recibe
    // CorrectIndex). Solo cuentan las preguntas de selección múltiple; si el
    // examen tiene alguna abierta, el intento queda pendiente de revisión
    // (Reviewed = false) hasta que el profesor ponga la nota final — igual que
    // hacía submitQuizAttempt en el frontend.
    public async Task<(QuizAttemptResponse? Result, string? Error)> SubmitAsync(long quizId, long studentId, QuizAttemptRequest request)
    {
        var questions = await _db.QuizQuestions
            .Where(q => q.QuizId == quizId)
            .OrderBy(q => q.SortOrder)
            .ToListAsync();

        if (questions.Count == 0)
        {
            return (null, "Este examen no tiene preguntas");
        }

        if (request.Answers.Count != questions.Count)
        {
            return (null, $"Se esperaban {questions.Count} respuestas y llegaron {request.Answers.Count}");
        }

        var entity = await _db.QuizAttempts.SingleOrDefaultAsync(a => a.QuizId == quizId && a.StudentId == studentId);
        if (entity is not null)
        {
            var retryError = ValidateRetry(entity);
            if (retryError is not null)
            {
                return (null, retryError);
            }
        }

        // Tiempo límite controlado en el servidor: el examen se debe haber
        // abierto con StartAsync, y la entrega tiene que llegar antes del
        // límite (más un margen de red). Fuera de tiempo, el intento queda
        // registrado con 0 — el profesor puede habilitar un reintento.
        var timeLimit = await _db.Quizzes.Where(q => q.Id == quizId).Select(q => q.TimeLimitMinutes).SingleAsync();
        var start = await _db.QuizAttemptStarts.FindAsync(quizId, studentId);
        var late = false;
        if (timeLimit is not null)
        {
            if (start is null)
            {
                return (null, "Este examen tiene tiempo límite; ábrelo antes de entregarlo.");
            }

            late = DateTime.UtcNow > start.StartedAt.AddMinutes(timeLimit.Value) + SubmitGrace;
        }

        List<object?> answers;
        if (late)
        {
            answers = questions.Select(_ => (object?)null).ToList();
        }
        else
        {
            var (normalized, answersError) = NormalizeAnswers(questions, request.Answers);
            if (answersError is not null)
            {
                return (null, answersError);
            }

            answers = normalized;
        }

        var hasOpen = questions.Any(q => q.QuestionType == "open");
        var gradable = questions.Select((q, i) => (q, i)).Where(x => x.q.QuestionType != "open").ToList();
        var correct = gradable.Count(x => answers[x.i] is int chosen && chosen == x.q.CorrectIndex);
        var score = late || gradable.Count == 0
            ? 0m
            : Math.Round((decimal)correct / gradable.Count * MaxScore, 1, MidpointRounding.AwayFromZero);

        // Un nuevo intento reemplaza el anterior (UNIQUE QuizId+StudentId).
        if (entity is null)
        {
            entity = new QuizAttempt { QuizId = quizId, StudentId = studentId };
            _db.QuizAttempts.Add(entity);
        }

        entity.AnswersJson = JsonSerializer.Serialize(answers);
        entity.Score = score;
        entity.Feedback = late ? "Entregado fuera del tiempo límite." : string.Empty;
        entity.Reviewed = late || !hasOpen;
        entity.RetryAllowed = false;
        // Si se autocalificó, el estudiante ya ve la nota al entregar — no hace
        // falta avisarle en la campanita. Si queda pendiente (o se anuló por
        // tiempo), el aviso sí tiene que llegarle.
        entity.Seen = !late && !hasOpen;
        entity.SubmittedAt = DateTime.UtcNow;

        // El inicio se descarta: un reintento autorizado arranca con el tiempo completo.
        if (start is not null)
        {
            _db.QuizAttemptStarts.Remove(start);
        }

        await _db.SaveChangesAsync();

        return (ToResponse(entity), null);
    }

    public async Task<QuizAttemptResponse?> ReviewAsync(long attemptId, ReviewQuizAttemptRequest request)
    {
        var entity = await _db.QuizAttempts.FindAsync(attemptId);
        if (entity is null)
        {
            return null;
        }

        entity.Score = request.Score;
        entity.Feedback = request.Feedback ?? string.Empty;
        entity.Reviewed = true;
        entity.RetryAllowed = request.RetryAllowed;
        entity.Seen = false;

        await _db.SaveChangesAsync();

        return ToResponse(entity);
    }

    // El profesor autoriza reintentar sin volver a calificar (allowRetry en el frontend).
    public async Task<QuizAttemptResponse?> AllowRetryAsync(long attemptId)
    {
        var entity = await _db.QuizAttempts.FindAsync(attemptId);
        if (entity is null)
        {
            return null;
        }

        entity.RetryAllowed = true;
        await _db.SaveChangesAsync();

        return ToResponse(entity);
    }

    // Filtrar por studentId hace de chequeo de pertenencia, igual que en Submission.
    public async Task<QuizAttemptResponse?> MarkSeenAsync(long attemptId, long studentId)
    {
        var entity = await _db.QuizAttempts.SingleOrDefaultAsync(a => a.Id == attemptId && a.StudentId == studentId);
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

    // Mismo criterio que el "locked" de QuizAttemptForm en el frontend.
    private static string? ValidateRetry(QuizAttempt entity)
    {
        if (!entity.Reviewed)
        {
            return "Tu examen está pendiente de revisión; no puedes volver a presentarlo todavía.";
        }

        if (entity.Score >= PassThreshold)
        {
            return "Ya aprobaste este examen.";
        }

        if (!entity.RetryAllowed)
        {
            return "Ya presentaste este examen. Pide a tu profesor que habilite un reintento.";
        }

        return null;
    }

    // Devuelve una lista con int (multiple), string (open) o null por pregunta.
    private static (List<object?> Answers, string? Error) NormalizeAnswers(List<QuizQuestion> questions, List<JsonElement> raw)
    {
        var result = new List<object?>(questions.Count);

        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var a = raw[i];
            var label = $"Respuesta {i + 1}";

            if (a.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                result.Add(null);
                continue;
            }

            if (q.QuestionType == "open")
            {
                if (a.ValueKind != JsonValueKind.String)
                {
                    return (result, $"{label}: la pregunta es abierta, se esperaba texto");
                }

                result.Add(a.GetString());
                continue;
            }

            var optionCount = q.OptionsJson is null ? 0 : JsonSerializer.Deserialize<List<string>>(q.OptionsJson)?.Count ?? 0;
            if (a.ValueKind != JsonValueKind.Number || !a.TryGetInt32(out var chosen) || chosen < 0 || chosen >= optionCount)
            {
                return (result, $"{label}: opción inválida");
            }

            result.Add(chosen);
        }

        return (result, null);
    }

    private static QuizAttemptResponse ToResponse(QuizAttempt a) => new()
    {
        Id = a.Id,
        QuizId = a.QuizId,
        StudentId = a.StudentId,
        Answers = JsonSerializer.Deserialize<List<JsonElement>>(a.AnswersJson) ?? new List<JsonElement>(),
        Score = a.Score,
        Feedback = a.Feedback,
        Reviewed = a.Reviewed,
        RetryAllowed = a.RetryAllowed,
        Seen = a.Seen,
        SubmittedAt = AsUtc(a.SubmittedAt),
    };

    // SQL Server devuelve DATETIME2 sin Kind; se guardan en UTC, así que se
    // marcan como tal para que el JSON lleve la "Z" y el navegador no las
    // interprete como hora local.
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
