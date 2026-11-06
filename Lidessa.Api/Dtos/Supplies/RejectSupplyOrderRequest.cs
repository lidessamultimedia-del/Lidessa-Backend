using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Supplies;

// Mismos campos que RejectOrderModal del frontend.
public class RejectSupplyOrderRequest
{
    [Required(ErrorMessage = "Escriba la causa del rechazo")]
    public string Reason { get; set; } = string.Empty;

    // URL devuelta por POST /api/files/attachments — solo si ya se le
    // devolvió el dinero al cliente.
    [MaxLength(500)]
    public string? RefundProofUrl { get; set; }
}
