namespace Lidessa.Api.Models;

// Producto del catálogo de Suministros (/tienda) — calcado de los productos
// de SuppliesContext en el frontend: { name, description, price, image,
// stock, active }.
public class SupplyProduct
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public int Stock { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<SupplyOrderItem> OrderItems { get; set; } = new List<SupplyOrderItem>();
}
