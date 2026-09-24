using System.Text.Json;

namespace Lidessa.Api.Dtos.QuizAttempts;

public class QuizAttemptRequest
{
    // Alineado por posición con las preguntas del examen (orden de SortOrder):
    // número (índice de la opción elegida) para "multiple", texto para "open",
    // o null si quedó sin responder (ej. se acabó el tiempo).
    public List<JsonElement> Answers { get; set; } = new();
}
