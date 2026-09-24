namespace Lidessa.Api.Dtos.Quizzes;

public class QuizQuestionRequest
{
    // "multiple" | "open"
    public string Type { get; set; } = "multiple";

    public string Text { get; set; } = string.Empty;

    // Solo para "multiple"; se ignora en "open".
    public List<string>? Options { get; set; }

    // Índice dentro de Options; solo para "multiple".
    public int? CorrectIndex { get; set; }
}
