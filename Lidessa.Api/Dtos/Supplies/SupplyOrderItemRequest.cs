using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Supplies;

public class SupplyOrderItemRequest
{
    [Required]
    public long ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "La cantidad debe ser al menos 1")]
    public int Quantity { get; set; }
}
