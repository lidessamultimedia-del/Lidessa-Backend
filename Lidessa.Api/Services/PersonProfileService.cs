using Lidessa.Api.Data;
using Lidessa.Api.Dtos.PersonProfiles;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class PersonProfileService
{
    private readonly AppDbContext _db;

    public PersonProfileService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<PersonProfileResponse>> GetAllAsync()
    {
        return await _db.PersonProfiles
            .Include(p => p.User)
            .Include(p => p.DocumentType)
            .OrderBy(p => p.FirstName).ThenBy(p => p.LastName)
            .Select(p => ToResponse(p))
            .ToListAsync();
    }

    public async Task<PersonProfileResponse?> GetByIdAsync(long id)
    {
        var entity = await _db.PersonProfiles
            .Include(p => p.User)
            .Include(p => p.DocumentType)
            .SingleOrDefaultAsync(p => p.Id == id);

        return entity is null ? null : ToResponse(entity);
    }

    public async Task<(PersonProfileResponse? Result, string? Error)> CreateAsync(PersonProfileCreateRequest request)
    {
        var userExists = await _db.Users.AnyAsync(u => u.Id == request.UserId);
        if (!userExists)
        {
            return (null, "El usuario indicado no existe");
        }

        var profileExists = await _db.PersonProfiles.AnyAsync(p => p.UserId == request.UserId);
        if (profileExists)
        {
            return (null, "Ese usuario ya tiene un perfil en el directorio");
        }

        if (request.DocumentTypeId is not null)
        {
            var docTypeExists = await _db.DocumentTypes.AnyAsync(d => d.Id == request.DocumentTypeId);
            if (!docTypeExists)
            {
                return (null, "El tipo de documento indicado no existe");
            }
        }

        var entity = new PersonProfile
        {
            UserId = request.UserId,
            FirstName = request.FirstName,
            LastName = request.LastName,
            DocumentTypeId = request.DocumentTypeId,
            DocumentNumber = request.DocumentNumber,
            CourseInterest = request.CourseInterest,
            JoinedDate = request.JoinedDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
        };

        _db.PersonProfiles.Add(entity);
        await _db.SaveChangesAsync();

        return (await GetByIdAsync(entity.Id), null);
    }

    public async Task<(PersonProfileResponse? Result, string? Error)> UpdateAsync(long id, PersonProfileUpdateRequest request)
    {
        var entity = await _db.PersonProfiles.FindAsync(id);
        if (entity is null)
        {
            return (null, "Perfil no encontrado");
        }

        if (request.DocumentTypeId is not null)
        {
            var docTypeExists = await _db.DocumentTypes.AnyAsync(d => d.Id == request.DocumentTypeId);
            if (!docTypeExists)
            {
                return (null, "El tipo de documento indicado no existe");
            }
        }

        entity.FirstName = request.FirstName;
        entity.LastName = request.LastName;
        entity.DocumentTypeId = request.DocumentTypeId;
        entity.DocumentNumber = request.DocumentNumber;
        entity.CourseInterest = request.CourseInterest;
        if (request.JoinedDate is not null)
        {
            entity.JoinedDate = request.JoinedDate.Value;
        }

        await _db.SaveChangesAsync();

        return (await GetByIdAsync(id), null);
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var entity = await _db.PersonProfiles.FindAsync(id);
        if (entity is null)
        {
            return false;
        }

        _db.PersonProfiles.Remove(entity);
        await _db.SaveChangesAsync();

        return true;
    }

    private static PersonProfileResponse ToResponse(PersonProfile p) => new()
    {
        Id = p.Id,
        UserId = p.UserId,
        UserName = p.User.Name,
        UserEmail = p.User.Email,
        UserRole = p.User.Role,
        AvatarUrl = p.User.AvatarUrl,
        FirstName = p.FirstName,
        LastName = p.LastName,
        DocumentTypeId = p.DocumentTypeId,
        DocumentTypeName = p.DocumentType?.Name,
        DocumentNumber = p.DocumentNumber,
        CourseInterest = p.CourseInterest,
        JoinedDate = p.JoinedDate,
    };
}
