using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Lidessa.Api.Dtos.Services;

public class ServiceRequest
{
    [Required]
    public long CategoryId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [MaxLength(500)]
    public string? HeroImageUrl { get; set; }

    public bool Active { get; set; } = true;

    // true = uno de los 16 servicios originales del sitio (contenido
    // protegido) — el frontend lo usa solo para mostrar un candado, no
    // cambia ninguna regla en el backend.
    public bool Locked { get; set; }

    // Estructura completa de pestañas/secciones/viñetas, tal cual la arma el
    // panel de admin — se guarda como texto JSON (TabsJson en la base).
    [Required]
    public JsonElement Tabs { get; set; }
}
