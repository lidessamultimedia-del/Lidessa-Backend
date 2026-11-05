using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Supplies;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

// Catálogo de Suministros — calcado de addProduct / updateProduct /
// deleteProduct / toggleProductActive de SuppliesContext en el frontend.
public class SupplyProductService
{
    private readonly AppDbContext _db;

    public SupplyProductService(AppDbContext db)
    {
        _db = db;
    }

    // Los más nuevos primero, igual que el frontend (addProduct los pone al
    // inicio de la lista). onlyActive = catálogo público (activeProducts).
    public async Task<List<SupplyProductResponse>> GetAllAsync(bool onlyActive)
    {
        var query = _db.SupplyProducts.AsNoTracking();
        if (onlyActive)
        {
            query = query.Where(p => p.Active);
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Select(p => ToResponse(p))
            .ToListAsync();
    }

    public async Task<SupplyProductResponse?> GetByIdAsync(long id)
    {
        var entity = await _db.SupplyProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<SupplyProductResponse> CreateAsync(SupplyProductRequest request)
    {
        var now = DateTime.UtcNow;
        var entity = new SupplyProduct
        {
            CreatedAt = now,
        };
        Apply(entity, request, now);

        _db.SupplyProducts.Add(entity);
        await _db.SaveChangesAsync();

        return ToResponse(entity);
    }

    public async Task<SupplyProductResponse?> UpdateAsync(long id, SupplyProductRequest request)
    {
        var entity = await _db.SupplyProducts.FindAsync(id);
        if (entity is null)
        {
            return null;
        }

        Apply(entity, request, DateTime.UtcNow);
        await _db.SaveChangesAsync();

        return ToResponse(entity);
    }

    // Ocultar/mostrar en el catálogo sin tener que mandar el producto completo.
    public async Task<SupplyProductResponse?> ToggleActiveAsync(long id)
    {
        var entity = await _db.SupplyProducts.FindAsync(id);
        if (entity is null)
        {
            return null;
        }

        entity.Active = !entity.Active;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ToResponse(entity);
    }

    // Los pedidos que ya tenían este producto no se tocan: sus líneas
    // guardan nombre y precio propios, y la FK queda en NULL (ON DELETE SET
    // NULL), igual que en el frontend, donde borrar un producto no altera
    // los pedidos existentes.
    public async Task<bool> DeleteAsync(long id)
    {
        var entity = await _db.SupplyProducts.FindAsync(id);
        if (entity is null)
        {
            return false;
        }

        _db.SupplyProducts.Remove(entity);
        await _db.SaveChangesAsync();

        return true;
    }

    private static void Apply(SupplyProduct entity, SupplyProductRequest request, DateTime now)
    {
        entity.Name = request.Name.Trim();
        entity.Description = request.Description?.Trim() ?? string.Empty;
        entity.Price = request.Price;
        entity.Stock = request.Stock;
        entity.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
        entity.Active = request.Active;
        entity.UpdatedAt = now;
    }

    private static SupplyProductResponse ToResponse(SupplyProduct p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Price = p.Price,
        ImageUrl = p.ImageUrl,
        Stock = p.Stock,
        Active = p.Active,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
    };
}
