using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Quizzes;

public class QuizRequest
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public DateTime DueDate { get; set; }

    public long? TopicId { get; set; }

    public DateOnly? PublishAt { get; set; }

    [Range(1, int.MaxValue)]
    public int? TimeLimitMinutes { get; set; }

    // Vacío = para todo el curso; con valores = solo esos estudiantes.
    public List<long> AssignedStudentIds { get; set; } = new();

    // Se reemplazan completas en cada guardado (igual que QuizFormModal en el
    // frontend, que siempre manda el examen entero). El orden de la lista es
    // el SortOrder de cada pregunta.
    public List<QuizQuestionRequest> Questions { get; set; } = new();
}
