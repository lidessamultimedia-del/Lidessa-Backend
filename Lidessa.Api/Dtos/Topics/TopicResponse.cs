namespace Lidessa.Api.Dtos.Topics;

public class TopicResponse
{
    public long Id { get; set; }
    public long CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
