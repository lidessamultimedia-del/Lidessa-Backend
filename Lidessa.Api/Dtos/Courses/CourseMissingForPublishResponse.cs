namespace Lidessa.Api.Dtos.Courses;

public class CourseMissingForPublishResponse
{
    public bool CanPublish { get; set; }
    public List<string> Missing { get; set; } = new();
}
