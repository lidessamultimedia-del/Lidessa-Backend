using System.ComponentModel.DataAnnotations;

namespace Lidessa.Api.Dtos.Supplies;

// Mismos campos y reglas que SupplyProductFormModal en el frontend: nombre
// obligatorio, precio > 0, stock >= 0 (vacío = 0).
public class SupplyProductRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    // Límites en cultura invariante: con la cultura del servidor (es-CO) "0.01"
    // no es un decimal válido y la validación reventaba con un 500.
    [Range(typeof(decimal), "0.01", "999999999999", ErrorMessage = "Ingresa un precio válido",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "El stock no puede ser negativo")]
    public int Stock { get; set; }

    // URL devuelta por POST /api/files/attachments (o una ruta de /assets).
    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    public bool Active { get; set; } = true;
}
