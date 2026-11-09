using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Supplies;

// Mismos campos y reglas que PaymentShortfallModal del frontend: monto que
// falta (> 0) y mensaje para el cliente.
public class PaymentShortfallRequest
{
    // Límites en cultura invariante (ver SupplyProductRequest.Price).
    [Range(typeof(decimal), "0.01", "999999999999", ErrorMessage = "Ingresa el monto que falta",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Escribe un mensaje para el cliente")]
    public string Message { get; set; } = string.Empty;
}
