namespace Lidessa.Api.Models;

// Pedido de la tienda de Suministros — calcado de createOrder() en
// SuppliesContext. Los objetos anidados del frontend (paymentNote, shipment)
// van aplanados en columnas propias porque siempre son 0 o 1 por pedido.
public class SupplyOrder
{
    public long Id { get; set; }

    // Cuenta de cliente que hizo el pedido. NULL si la cuenta se borró
    // después (el pedido se conserva para el historial de ventas).
    public long? ClientId { get; set; }

    // Datos de contacto y entrega tal cual los escribió el cliente en el
    // checkout (es una copia, no se leen de la cuenta: pueden ser distintos).
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    public decimal Total { get; set; }

    // Foto del comprobante de pago (revisión manual, no hay pasarela).
    public string? ProofImageUrl { get; set; }

    // pending | confirmed | shipped | completed | rejected (ver SupplyOrderStatuses).
    public string Status { get; set; } = SupplyOrderStatuses.Pending;
    public DateTime? StatusUpdatedAt { get; set; }

    // paymentNote: el comprobante no cubre el total y falta este monto.
    public decimal? PaymentNoteAmount { get; set; }
    public string? PaymentNoteMessage { get; set; }
    public DateTime? PaymentNoteDate { get; set; }

    // shipment: datos del despacho (transportadora, guía, foto del paquete).
    public string? ShipmentCarrier { get; set; }
    public string? ShipmentTrackingNumber { get; set; }
    public string? ShipmentPhotoUrl { get; set; }
    public DateTime? ShippedAt { get; set; }

    // Solo si se rechazó.
    public string? RejectionReason { get; set; }
    public string? RefundProofUrl { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AppUser? Client { get; set; }
    public ICollection<SupplyOrderItem> Items { get; set; } = new List<SupplyOrderItem>();
    public ICollection<SupplyOrderStatusChange> StatusHistory { get; set; } = new List<SupplyOrderStatusChange>();
}

public static class SupplyOrderStatuses
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Shipped = "shipped";
    public const string Completed = "completed";
    public const string Rejected = "rejected";

    public static readonly string[] All = { Pending, Confirmed, Shipped, Completed, Rejected };

    // Igual que STOCK_HOLDING_STATUSES del frontend: mientras el pedido está
    // en uno de estos estados, sus unidades siguen apartadas del inventario.
    public static readonly string[] HoldingStock = { Pending, Confirmed };
}
