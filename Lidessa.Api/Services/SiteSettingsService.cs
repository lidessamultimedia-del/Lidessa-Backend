using Lidessa.Api.Data;
using Lidessa.Api.Dtos.SiteSettings;
using Lidessa.Api.Models;

namespace Lidessa.Api.Services;

// SiteSettings es una tabla de una sola fila (Id = 1, con CHECK en la base).
// Si por alguna razón la fila no existe (base creada a mano), se crea al
// guardar en vez de fallar.
public class SiteSettingsService
{
    private const int SingletonId = 1;

    private readonly AppDbContext _db;

    public SiteSettingsService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<SiteSettingsResponse> GetAsync()
    {
        var entity = await _db.SiteSettings.FindAsync(SingletonId);
        return ToResponse(entity ?? new SiteSettings { Id = SingletonId });
    }

    public async Task<SiteSettingsResponse> UpdateAsync(SiteSettingsRequest request)
    {
        var entity = await _db.SiteSettings.FindAsync(SingletonId);
        if (entity is null)
        {
            entity = new SiteSettings { Id = SingletonId };
            _db.SiteSettings.Add(entity);
        }

        entity.Phone = request.Phone.Trim();
        entity.Email = request.Email.Trim();
        entity.Address = request.Address.Trim();
        entity.Schedule = request.Schedule.Trim();
        await _db.SaveChangesAsync();

        return ToResponse(entity);
    }

    private static SiteSettingsResponse ToResponse(SiteSettings s) => new()
    {
        Phone = s.Phone,
        Email = s.Email,
        Address = s.Address,
        Schedule = s.Schedule,
    };
}
