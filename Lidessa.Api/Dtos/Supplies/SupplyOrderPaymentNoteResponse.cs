namespace Lidessa.Api.Dtos.Supplies;

// paymentNote del frontend: { amount, message, date }.
public class SupplyOrderPaymentNoteResponse
{
    public decimal Amount { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}
