using System.Text.RegularExpressions;
using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Courses;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class CourseService
{
    private static readonly string[] AllowedFormats = { "topics", "weekly" };
    private static readonly Regex ShortNamePattern = new("^[A-Za-z0-9]+(-[A-Za-z0-9]+)*$", RegexOptions.Compiled);
    private const int PasswordMinLength = 6;
    private const int MinActivitiesToPublish = 3;
    private const int MinExamsToPublish = 1;

    private readonly AppDbContext _db;

    public CourseService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<CourseResponse>> GetAllAsync()
    {
        return await _db.Courses
            .Include(c => c.Teacher)
            .OrderBy(c => c.Name)
            .Select(c => ToResponse(c))
            .ToListAsync();
    }

    public async Task<CourseResponse?> GetByIdAsync(long id)
    {
        var entity = await _db.Courses
            .Include(c => c.Teacher)
            .SingleOrDefaultAsync(c => c.Id == id);

        return entity is null ? null : ToResponse(entity);
    }

    // Calcado de courseMissingForPublish del frontend: lista en español lo que
    // le falta al curso para poder publicarse (lista vacía = ya puede).
    public async Task<List<string>> GetMissingForPublishAsync(long courseId)
    {
        var teacherId = await _db.Courses.Where(c => c.Id == courseId).Select(c => c.TeacherId).SingleOrDefaultAsync();
        var activitiesCount = await _db.Assignments.CountAsync(a => a.CourseId == courseId);
        var examsCount = await _db.Quizzes.CountAsync(q => q.CourseId == courseId);

        return BuildMissingForPublish(teacherId is not null, activitiesCount, examsCount);
    }

    private static List<string> BuildMissingForPublish(bool hasTeacher, int activitiesCount, int examsCount)
    {
        var missing = new List<string>();

        if (!hasTeacher)
        {
            missing.Add("Asignar un profesor al curso");
        }

        if (activitiesCount < MinActivitiesToPublish)
        {
            missing.Add($"Agregar al menos {MinActivitiesToPublish} actividades (tiene {activitiesCount})");
        }

        if (examsCount < MinExamsToPublish)
        {
            missing.Add($"Agregar al menos {MinExamsToPublish} examen (tiene {examsCount})");
        }

        return missing;
    }

    public async Task<(CourseResponse? Result, string? Error)> CreateAsync(CourseRequest request)
    {
        var error = await ValidateAsync(request, courseId: null);
        if (error is not null)
        {
            return (null, error);
        }

        if (request.Published)
        {
            // Un curso recién creado nunca tiene actividades/exámenes todavía.
            var missing = BuildMissingForPublish(request.TeacherId is not null, activitiesCount: 0, examsCount: 0);
            if (missing.Count > 0)
            {
                return (null, $"No se puede publicar el curso: {string.Join("; ", missing)}");
            }
        }

        var entity = new Course
        {
            Name = request.Name.Trim(),
            ShortName = request.ShortName?.Trim() ?? string.Empty,
            Description = request.Description?.Trim() ?? string.Empty,
            Category = request.Category?.Trim() ?? string.Empty,
            TeacherId = request.TeacherId,
            Format = request.Format,
            Capacity = request.Capacity,
            Color = request.Color,
            ImageUrl = request.Image,
            RequiresPassword = request.RequiresPassword,
            PasswordHash = request.RequiresPassword ? BCrypt.Net.BCrypt.HashPassword(request.Password) : null,
            SelfEnrollment = request.SelfEnrollment,
            GuestAccess = request.GuestAccess,
            Published = request.Published,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.Courses.Add(entity);
        await _db.SaveChangesAsync();

        return (await GetByIdAsync(entity.Id), null);
    }

    public async Task<(CourseResponse? Result, string? Error)> UpdateAsync(long id, CourseRequest request)
    {
        var entity = await _db.Courses.FindAsync(id);
        if (entity is null)
        {
            return (null, "Curso no encontrado");
        }

        var error = await ValidateAsync(request, courseId: id);
        if (error is not null)
        {
            return (null, error);
        }

        if (request.Published && !entity.Published)
        {
            // Se usa request.TeacherId (no el de la entidad todavía sin
            // actualizar) para permitir asignar profesor y publicar en el
            // mismo request.
            var activitiesCount = await _db.Assignments.CountAsync(a => a.CourseId == id);
            var examsCount = await _db.Quizzes.CountAsync(q => q.CourseId == id);
            var missing = BuildMissingForPublish(request.TeacherId is not null, activitiesCount, examsCount);
            if (missing.Count > 0)
            {
                return (null, $"No se puede publicar el curso: {string.Join("; ", missing)}");
            }
        }

        entity.Name = request.Name.Trim();
        entity.ShortName = request.ShortName?.Trim() ?? string.Empty;
        entity.Description = request.Description?.Trim() ?? string.Empty;
        entity.Category = request.Category?.Trim() ?? string.Empty;
        entity.TeacherId = request.TeacherId;
        entity.Format = request.Format;
        entity.Capacity = request.Capacity;
        entity.Color = request.Color;
        entity.ImageUrl = request.Image;
        entity.RequiresPassword = request.RequiresPassword;
        if (request.RequiresPassword)
        {
            // Se conserva el hash existente si no se envía una contraseña nueva
            // (el backend nunca devuelve la contraseña en texto plano al formulario).
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                entity.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            }
        }
        else
        {
            entity.PasswordHash = null;
        }
        entity.SelfEnrollment = request.SelfEnrollment;
        entity.GuestAccess = request.GuestAccess;
        entity.Published = request.Published;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return (await GetByIdAsync(id), null);
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var entity = await _db.Courses.FindAsync(id);
        if (entity is null)
        {
            return false;
        }

        _db.Courses.Remove(entity);
        await _db.SaveChangesAsync();

        return true;
    }

    private async Task<string?> ValidateAsync(CourseRequest request, long? courseId)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "El nombre del curso es obligatorio";
        }

        if (!string.IsNullOrWhiteSpace(request.ShortName) && !ShortNamePattern.IsMatch(request.ShortName.Trim()))
        {
            return "El nombre corto solo puede tener letras, números y guiones (ej. LIDER-001)";
        }

        if (request.Capacity <= 0 || request.Capacity > 10000)
        {
            return "La capacidad debe estar entre 1 y 10000";
        }

        if (!AllowedFormats.Contains(request.Format))
        {
            return $"Format debe ser uno de: {string.Join(", ", AllowedFormats)}";
        }

        if (request.RequiresPassword)
        {
            var hasExistingHash = courseId is not null
                && await _db.Courses.AnyAsync(c => c.Id == courseId && c.PasswordHash != null);

            if (string.IsNullOrWhiteSpace(request.Password) && !hasExistingHash)
            {
                return "Ingrese la contraseña de acceso";
            }

            if (!string.IsNullOrWhiteSpace(request.Password) && request.Password.Trim().Length < PasswordMinLength)
            {
                return $"La contraseña debe tener al menos {PasswordMinLength} caracteres";
            }
        }

        if (request.TeacherId is not null)
        {
            var teacherExists = await _db.Users.AnyAsync(u => u.Id == request.TeacherId && u.Role == "profesor");
            if (!teacherExists)
            {
                return "El profesor indicado no existe";
            }
        }

        return null;
    }

    private static CourseResponse ToResponse(Course c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        ShortName = c.ShortName,
        Description = c.Description,
        Category = c.Category,
        TeacherId = c.TeacherId,
        TeacherName = c.Teacher?.Name,
        Format = c.Format,
        Capacity = c.Capacity,
        Color = c.Color,
        Image = c.ImageUrl,
        RequiresPassword = c.RequiresPassword,
        SelfEnrollment = c.SelfEnrollment,
        GuestAccess = c.GuestAccess,
        Published = c.Published,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };
}
