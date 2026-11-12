using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Supplies;

// Igual que SupplyOrderRequest, salvo Items: null = no tocar los artículos
// (OrderFormModal los omite cuando el pedido ya no los deja editar — enviado,
// completado o rechazado); una lista = reemplazarlos y ajustar el stock por
// la diferencia con los que tenía.
public class UpdateSupplyOrderRequest
{
    public long? ClientId { get; set; }

    [Required(ErrorMessage = "El nombre del cliente es obligatorio")]
    [MaxLength(150)]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El teléfono es obligatorio")]
    [MaxLength(30)]
    public string CustomerPhone { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? CustomerEmail { get; set; }

    [Required(ErrorMessage = "La dirección de envío es obligatoria")]
    [MaxLength(300)]
    public string Address { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? City { get; set; }

    public string? Notes { get; set; }

    [MaxLength(500)]
    public string? ProofImageUrl { get; set; }

    public List<SupplyOrderItemRequest>? Items { get; set; }
}
