using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Enrollments;

public class EnrollmentRequest
{
    [Required]
    public long StudentId { get; set; }
}
