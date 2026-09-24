namespace Lidessa.Api.Dtos.Quizzes;

public class QuizQuestionResponse
{
    public long Id { get; set; }
    public int SortOrder { get; set; }
    public string Type { get; set; } = "multiple";
    public string Text { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();

    // Null para preguntas abiertas, y también cuando quien consulta no es
    // admin ni el profesor dueño (el estudiante no debe ver la respuesta).
    public int? CorrectIndex { get; set; }
}
