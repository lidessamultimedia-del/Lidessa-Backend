using Lidessa.Api.Data;
using Lidessa.Api.Dtos.DocumentTypes;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class DocumentTypeService
{
    private readonly AppDbContext _db;

    public DocumentTypeService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<DocumentTypeResponse>> GetAllAsync()
    {
        return await _db.DocumentTypes
            .OrderBy(d => d.Name)
            .Select(d => ToResponse(d))
            .ToListAsync();
    }

    public async Task<DocumentTypeResponse?> GetByIdAsync(long id)
    {
        var entity = await _db.DocumentTypes.FindAsync(id);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<(DocumentTypeResponse? Result, string? Error)> CreateAsync(DocumentTypeRequest request)
    {
        var nameTaken = await _db.DocumentTypes.AnyAsync(d => d.Name == request.Name);
        if (nameTaken)
        {
            return (null, "Ya existe un tipo de documento con ese nombre");
        }

        var entity = new DocumentType { Name = request.Name };
        _db.DocumentTypes.Add(entity);
        await _db.SaveChangesAsync();

        return (ToResponse(entity), null);
    }

    public async Task<(DocumentTypeResponse? Result, string? Error)> UpdateAsync(long id, DocumentTypeRequest request)
    {
        var entity = await _db.DocumentTypes.FindAsync(id);
        if (entity is null)
        {
            return (null, "Tipo de documento no encontrado");
        }

        var nameTaken = await _db.DocumentTypes.AnyAsync(d => d.Name == request.Name && d.Id != id);
        if (nameTaken)
        {
            return (null, "Ya existe un tipo de documento con ese nombre");
        }

        entity.Name = request.Name;
        await _db.SaveChangesAsync();

        return (ToResponse(entity), null);
    }

    public async Task<(bool Deleted, string? Error)> DeleteAsync(long id)
    {
        var entity = await _db.DocumentTypes.FindAsync(id);
        if (entity is null)
        {
            return (false, "Tipo de documento no encontrado");
        }

        var inUse = await _db.PersonProfiles.AnyAsync(p => p.DocumentTypeId == id);
        if (inUse)
        {
            return (false, "No se puede eliminar: está en uso por uno o más perfiles del directorio");
        }

        _db.DocumentTypes.Remove(entity);
        await _db.SaveChangesAsync();

        return (true, null);
    }

    private static DocumentTypeResponse ToResponse(DocumentType d) => new()
    {
        Id = d.Id,
        Name = d.Name,
    };
}
