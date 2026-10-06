using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Certifications;

public class CertificationRequest
{
    [Required]
    public long StudentId { get; set; }

    [Required]
    public long CourseId { get; set; }
}
