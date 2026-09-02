namespace Lidessa.Api.Models;

public class PersonProfile
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public long? DocumentTypeId { get; set; }
    public string? DocumentNumber { get; set; }
    public string? CourseInterest { get; set; }
    public DateOnly JoinedDate { get; set; }

    public AppUser User { get; set; } = null!;
    public DocumentType? DocumentType { get; set; }
}
