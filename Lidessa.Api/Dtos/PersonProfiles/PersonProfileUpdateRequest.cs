using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.PersonProfiles;

public class PersonProfileUpdateRequest
{
    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    public long? DocumentTypeId { get; set; }

    [MaxLength(40)]
    public string? DocumentNumber { get; set; }

    [MaxLength(200)]
    public string? CourseInterest { get; set; }

    public DateOnly? JoinedDate { get; set; }
}
