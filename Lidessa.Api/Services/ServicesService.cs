using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Services;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

// Maneja el modelo Service (los servicios publicados en /servicios) — se
// llama "ServicesService" en plural para no chocar con el nombre del modelo
// "Service".
public class ServicesService
{
    private readonly AppDbContext _db;

    public ServicesService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<ServiceResponse>> GetAllAsync()
    {
        var entities = await _db.Services
            .Include(s => s.Category)
            .OrderBy(s => s.Title)
            .ToListAsync();

        return entities.Select(ToResponse).ToList();
    }

    public async Task<ServiceResponse?> GetBySlugAsync(string slug)
    {
        var entity = await _db.Services.Include(s => s.Category).FirstOrDefaultAsync(s => s.Slug == slug);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<ServiceResponse?> GetByIdAsync(long id)
    {
        var entity = await _db.Services.Include(s => s.Category).FirstOrDefaultAsync(s => s.Id == id);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<bool> ExistsAsync(long id)
    {
        return await _db.Services.AnyAsync(s => s.Id == id);
    }

    public async Task<(ServiceResponse? Result, string? Error)> CreateAsync(ServiceRequest request)
    {
        // [Required] no detecta un JsonElement "vacío" (es un struct, nunca es
        // null) — se valida aparte para no reventar en GetRawText() más abajo.
        if (!HasTabs(request, out var tabsError))
        {
            return (null, tabsError);
        }

        var category = await _db.ServiceCategories.FindAsync(request.CategoryId);
        if (category is null)
        {
            return (null, "La categoría indicada no existe");
        }

        var entity = new Service
        {
            Slug = await NextSlugAsync(request.Title),
            CategoryId = request.CategoryId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            HeroImageUrl = request.HeroImageUrl,
            Active = request.Active,
            Locked = request.Locked,
            TabsJson = request.Tabs.GetRawText(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.Services.Add(entity);
        await _db.SaveChangesAsync();
        entity.Category = category;

        return (ToResponse(entity), null);
    }

    public async Task<(ServiceResponse? Result, string? Error)> UpdateAsync(long id, ServiceRequest request)
    {
        if (!HasTabs(request, out var tabsError))
        {
            return (null, tabsError);
        }

        var entity = await _db.Services.Include(s => s.Category).FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null)
        {
            return (null, "Servicio no encontrado");
        }

        var category = await _db.ServiceCategories.FindAsync(request.CategoryId);
        if (category is null)
        {
            return (null, "La categoría indicada no existe");
        }

        entity.CategoryId = request.CategoryId;
        entity.Category = category;
        entity.Title = request.Title.Trim();
        entity.Description = request.Description?.Trim() ?? string.Empty;
        entity.HeroImageUrl = request.HeroImageUrl;
        entity.Active = request.Active;
        entity.Locked = request.Locked;
        entity.TabsJson = request.Tabs.GetRawText();
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (ToResponse(entity), null);
    }

    public async Task<(bool Deleted, string? Error)> DeleteAsync(long id)
    {
        var entity = await _db.Services.FindAsync(id);
        if (entity is null)
        {
            return (false, "Servicio no encontrado");
        }

        if (entity.Locked)
        {
            return (false, "Este servicio es contenido protegido y no se puede eliminar");
        }

        _db.Services.Remove(entity);
        await _db.SaveChangesAsync();

        return (true, null);
    }

    private static bool HasTabs(ServiceRequest request, out string? error)
    {
        if (request.Tabs.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            error = "Falta la estructura de pestañas (tabs)";
            return false;
        }

        error = null;
        return true;
    }

    // Calcado de slugify() del frontend: minúsculas, sin tildes, guiones en
    // vez de espacios/símbolos. Si ya existe, se le suma un consecutivo
    // (-2, -3...) en vez del Date.now() que usa el mock, para que quede
    // legible en la URL real.
    private async Task<string> NextSlugAsync(string title)
    {
        var baseSlug = Slugify(title);
        var slug = baseSlug;
        for (var suffix = 2; await _db.Services.AnyAsync(s => s.Slug == slug); suffix++)
        {
            slug = $"{baseSlug}-{suffix}";
        }

        return slug;
    }

    private static string Slugify(string text)
    {
        var normalized = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var withoutDiacritics = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                withoutDiacritics.Append(c);
            }
        }

        var slug = Regex.Replace(withoutDiacritics.ToString(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length > 0 ? slug : "servicio";
    }

    private static ServiceResponse ToResponse(Service s) => new()
    {
        Id = s.Id,
        Slug = s.Slug,
        CategoryId = s.CategoryId,
        CategoryName = s.Category?.Name ?? string.Empty,
        Title = s.Title,
        Description = s.Description,
        HeroImageUrl = s.HeroImageUrl,
        Active = s.Active,
        Locked = s.Locked,
        Tabs = JsonDocument.Parse(string.IsNullOrWhiteSpace(s.TabsJson) ? "[]" : s.TabsJson).RootElement,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt,
    };
}
