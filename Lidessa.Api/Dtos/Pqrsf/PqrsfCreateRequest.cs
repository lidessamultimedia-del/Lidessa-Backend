using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Pqrsf;

// Formulario público de PQRSF. Nombre, correo y teléfono son opcionales:
// sin correo (y sin sesión iniciada) el ticket queda anónimo. Si quien envía
// tiene sesión, esos tres campos se ignoran y se toman de su cuenta.
public class PqrsfCreateRequest
{
    [Required]
    [MaxLength(20)]
    public string TicketType { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Name { get; set; }

    [MaxLength(256)]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [Required]
    [MaxLength(300)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Message { get; set; } = string.Empty;
}
