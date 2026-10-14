using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Services;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class ServiceCategoryService
{
    private readonly AppDbContext _db;

    public ServiceCategoryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<ServiceCategoryResponse>> GetAllAsync()
    {
        return await _db.ServiceCategories
            .OrderBy(c => c.Name)
            .Select(c => ToResponse(c))
            .ToListAsync();
    }

    public async Task<ServiceCategoryResponse?> GetByIdAsync(long id)
    {
        var entity = await _db.ServiceCategories.FindAsync(id);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<(ServiceCategoryResponse? Result, string? Error)> CreateAsync(ServiceCategoryRequest request)
    {
        var nameTaken = await _db.ServiceCategories.AnyAsync(c => c.Name == request.Name);
        if (nameTaken)
        {
            return (null, "Ya existe una categoría con ese nombre");
        }

        var entity = new ServiceCategory { Name = request.Name, Active = request.Active };
        _db.ServiceCategories.Add(entity);
        await _db.SaveChangesAsync();

        return (ToResponse(entity), null);
    }

    public async Task<(ServiceCategoryResponse? Result, string? Error)> UpdateAsync(long id, ServiceCategoryRequest request)
    {
        var entity = await _db.ServiceCategories.FindAsync(id);
        if (entity is null)
        {
            return (null, "Categoría no encontrada");
        }

        var nameTaken = await _db.ServiceCategories.AnyAsync(c => c.Name == request.Name && c.Id != id);
        if (nameTaken)
        {
            return (null, "Ya existe una categoría con ese nombre");
        }

        entity.Name = request.Name;
        entity.Active = request.Active;
        await _db.SaveChangesAsync();

        return (ToResponse(entity), null);
    }

    public async Task<(bool Deleted, string? Error)> DeleteAsync(long id)
    {
        var entity = await _db.ServiceCategories.FindAsync(id);
        if (entity is null)
        {
            return (false, "Categoría no encontrada");
        }

        var inUse = await _db.Services.AnyAsync(s => s.CategoryId == id);
        if (inUse)
        {
            return (false, "No se puede eliminar: hay servicios publicados en esta categoría");
        }

        _db.ServiceCategories.Remove(entity);
        await _db.SaveChangesAsync();

        return (true, null);
    }

    private static ServiceCategoryResponse ToResponse(ServiceCategory c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Active = c.Active,
    };
}
