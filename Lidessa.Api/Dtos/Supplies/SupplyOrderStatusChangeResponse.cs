namespace Lidessa.Api.Dtos.Supplies;

public class SupplyOrderStatusChangeResponse
{
    public string Status { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
}
