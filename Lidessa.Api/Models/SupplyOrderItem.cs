namespace Lidessa.Api.Models;

// Línea de un pedido: { productId, name, price, qty } en el frontend. Nombre
// y precio se copian al momento de comprar, así el pedido no cambia si
// después se edita o se borra el producto.
public class SupplyOrderItem
{
    public long Id { get; set; }
    public long OrderId { get; set; }

    // NULL si el producto se eliminó del catálogo después del pedido.
    public long? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    public SupplyOrder Order { get; set; } = null!;
    public SupplyProduct? Product { get; set; }
}
