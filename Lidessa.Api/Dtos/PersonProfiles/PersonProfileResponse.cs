namespace Lidessa.Api.Dtos.PersonProfiles;

public class PersonProfileResponse
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public long? DocumentTypeId { get; set; }
    public string? DocumentTypeName { get; set; }
    public string? DocumentNumber { get; set; }
    public string? CourseInterest { get; set; }
    public DateOnly JoinedDate { get; set; }
}
