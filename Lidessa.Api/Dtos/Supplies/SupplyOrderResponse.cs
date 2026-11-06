namespace Lidessa.Api.Dtos.Supplies;

public class SupplyOrderResponse
{
    public long Id { get; set; }
    public long? ClientId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? ProofImageUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? StatusUpdatedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? RefundProofUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<SupplyOrderItemResponse> Items { get; set; } = new();
    public List<SupplyOrderStatusChangeResponse> StatusHistory { get; set; } = new();
}
