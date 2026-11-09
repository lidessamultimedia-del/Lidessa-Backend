using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Supplies;

// Mismos campos y reglas que ShipOrderModal del frontend: transportadora,
// número de guía y foto del paquete, los tres obligatorios.
public class ShipSupplyOrderRequest
{
    [Required(ErrorMessage = "Indica la transportadora")]
    [MaxLength(100)]
    public string Carrier { get; set; } = string.Empty;

    [Required(ErrorMessage = "El número de guía es obligatorio")]
    [MaxLength(100)]
    public string TrackingNumber { get; set; } = string.Empty;

    // URL devuelta por POST /api/files/attachments.
    [Required(ErrorMessage = "Sube una foto del paquete")]
    [MaxLength(500)]
    public string PhotoUrl { get; set; } = string.Empty;
}
