using Lidessa.Api.Dtos.Files;

namespace Lidessa.Api.Services;

// Adjuntos genéricos (material de lección, guía de tarea, etc.): a diferencia
// de AvatarService no queda ligado a ningún usuario/entidad, solo guarda el
// archivo y devuelve su URL para que el caller la persista donde corresponda
// (Lesson.AttachmentUrl, Assignment.AttachmentUrl, ...).
public class AttachmentService
{
    private static readonly string[] AllowedExtensions =
    {
        ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx",
        ".jpg", ".jpeg", ".png", ".webp", ".gif",
    };
    private const long MaxFileSizeBytes = 20 * 1024 * 1024;
    private const string RelativeFolder = "uploads/attachments";

    private readonly IWebHostEnvironment _env;

    public AttachmentService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<(AttachmentUploadResponse? Result, string? Error)> UploadAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return (null, "Debe adjuntar un archivo");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return (null, $"El archivo supera el tamaño máximo permitido ({MaxFileSizeBytes / 1024 / 1024} MB)");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return (null, $"Formato no permitido. Use uno de: {string.Join(", ", AllowedExtensions)}");
        }

        var uploadsRoot = Path.Combine(_env.WebRootPath, RelativeFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(uploadsRoot);

        var storedFileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadsRoot, storedFileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return (new AttachmentUploadResponse
        {
            FileName = file.FileName,
            Url = $"/{RelativeFolder}/{storedFileName}",
            SizeBytes = file.Length,
        }, null);
    }
}
