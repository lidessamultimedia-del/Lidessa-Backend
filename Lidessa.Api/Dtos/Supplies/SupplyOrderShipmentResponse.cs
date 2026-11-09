namespace Lidessa.Api.Dtos.Supplies;

// shipment del frontend: { carrier, trackingNumber, photo, date }.
public class SupplyOrderShipmentResponse
{
    public string Carrier { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public DateTime Date { get; set; }
}
