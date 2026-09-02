namespace Lidessa.Api.Models;

public class QuizQuestion
{
    public long Id { get; set; }
    public string? LegacyId { get; set; }
    public long QuizId { get; set; }
    public int SortOrder { get; set; }
    public string QuestionType { get; set; } = "multiple";
    public string QuestionText { get; set; } = string.Empty;
    public string? OptionsJson { get; set; }
    public int? CorrectIndex { get; set; }

    public Quiz Quiz { get; set; } = null!;
}
