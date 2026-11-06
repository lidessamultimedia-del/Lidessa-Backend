namespace Lidessa.Api.Dtos.Supplies;

public class SupplyOrderItemResponse
{
    public long Id { get; set; }
    public long? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
