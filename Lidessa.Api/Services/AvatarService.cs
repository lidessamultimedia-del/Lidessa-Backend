using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Files;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class AvatarService
{
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;
    private const string RelativeFolder = "uploads/avatars";

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public AvatarService(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public async Task<(AvatarUploadResponse? Result, string? Error)> UploadAvatarAsync(long userId, IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return (null, "Debe adjuntar un archivo");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return (null, "El archivo supera el tamaño máximo permitido (5 MB)");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension) || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return (null, $"Formato no permitido. Use una imagen: {string.Join(", ", AllowedExtensions)}");
        }

        var user = await _db.Users.FindAsync(userId);
        if (user is null)
        {
            return (null, "Usuario no encontrado");
        }

        var uploadsRoot = Path.Combine(_env.WebRootPath, RelativeFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(uploadsRoot);

        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadsRoot, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        DeletePreviousAvatar(user.AvatarUrl, uploadsRoot);

        var avatarUrl = $"/{RelativeFolder}/{fileName}";
        user.AvatarUrl = avatarUrl;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (new AvatarUploadResponse { AvatarUrl = avatarUrl }, null);
    }

    private static void DeletePreviousAvatar(string? previousUrl, string uploadsRoot)
    {
        if (string.IsNullOrWhiteSpace(previousUrl) || !previousUrl.StartsWith($"/{RelativeFolder}/", StringComparison.Ordinal))
        {
            return;
        }

        var previousPath = Path.Combine(uploadsRoot, Path.GetFileName(previousUrl));
        if (File.Exists(previousPath))
        {
            File.Delete(previousPath);
        }
    }
}
