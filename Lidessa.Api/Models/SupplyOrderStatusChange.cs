namespace Lidessa.Api.Models;

// Una entrada de statusHistory ({ status, date }) — la línea de tiempo que
// muestra el detalle del pedido.
public class SupplyOrderStatusChange
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }

    public SupplyOrder Order { get; set; } = null!;
}
