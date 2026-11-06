using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Supplies;

// Mismos campos que createOrder() en SuppliesContext. A diferencia del
// frontend (que copia name/price del producto elegido en el formulario), el
// servidor vuelve a resolver nombre y precio desde el catálogo actual por
// cada ítem — así el cliente no puede mandar un precio distinto al real.
public class SupplyOrderRequest
{
    // Solo la usa el admin para ligar el pedido a una cuenta de cliente ya
    // registrada (select "Cliente registrado" en OrderFormModal). Si el
    // caller es un cliente, el servicio ignora este campo y usa su propio Id.
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

    // URL devuelta por POST /api/files/attachments (comprobante de pago).
    [MaxLength(500)]
    public string? ProofImageUrl { get; set; }

    [MinLength(1, ErrorMessage = "Agregue al menos un producto")]
    public List<SupplyOrderItemRequest> Items { get; set; } = new();
}
