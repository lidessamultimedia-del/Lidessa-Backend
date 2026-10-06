using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Certifications;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

// El certificado se entrega presencial: aquí solo se deja constancia de que el
// admin ya contactó al estudiante. Desmarcar borra la fila y el estudiante
// vuelve a la lista de "listos para certificar".
public class CertificationService
{
    private readonly AppDbContext _db;

    public CertificationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<CertificationResponse>> GetHistoryAsync(long? courseId, long? studentId)
    {
        var query = _db.Certifications
            .Include(c => c.Student)
            .Include(c => c.Course)
            .AsQueryable();

        if (courseId is not null)
        {
            query = query.Where(c => c.CourseId == courseId);
        }

        if (studentId is not null)
        {
            query = query.Where(c => c.StudentId == studentId);
        }

        return await query
            .OrderByDescending(c => c.MarkedAt)
            .Select(c => ToResponse(c))
            .ToListAsync();
    }

    public async Task<CertificationResponse?> GetByIdAsync(long id)
    {
        var entity = await _db.Certifications
            .Include(c => c.Student)
            .Include(c => c.Course)
            .SingleOrDefaultAsync(c => c.Id == id);

        return entity is null ? null : ToResponse(entity);
    }

    public async Task<(CertificationResponse? Result, string? Error)> MarkAsync(CertificationRequest request)
    {
        var course = await _db.Courses.FindAsync(request.CourseId);
        if (course is null)
        {
            return (null, "Curso no encontrado");
        }

        var student = await _db.Users.SingleOrDefaultAsync(u => u.Id == request.StudentId);
        if (student is null || student.Role != "estudiante")
        {
            return (null, "El estudiante indicado no existe");
        }

        var enrolled = await _db.CourseEnrollments.AnyAsync(e => e.CourseId == request.CourseId && e.StudentId == request.StudentId);
        if (!enrolled)
        {
            return (null, "El estudiante no está inscrito en este curso");
        }

        var alreadyMarked = await _db.Certifications.AnyAsync(c => c.CourseId == request.CourseId && c.StudentId == request.StudentId);
        if (alreadyMarked)
        {
            return (null, "El estudiante ya está marcado como certificado en este curso");
        }

        var entity = new Certification
        {
            StudentId = request.StudentId,
            CourseId = request.CourseId,
            MarkedAt = DateTime.UtcNow,
        };

        _db.Certifications.Add(entity);
        await _db.SaveChangesAsync();

        entity.Student = student;
        entity.Course = course;
        return (ToResponse(entity), null);
    }

    public async Task<bool> UnmarkAsync(long id)
    {
        var entity = await _db.Certifications.FindAsync(id);
        if (entity is null)
        {
            return false;
        }

        _db.Certifications.Remove(entity);
        await _db.SaveChangesAsync();

        return true;
    }

    private static CertificationResponse ToResponse(Certification c) => new()
    {
        Id = c.Id,
        StudentId = c.StudentId,
        StudentName = c.Student.Name,
        StudentEmail = c.Student.Email,
        CourseId = c.CourseId,
        CourseName = c.Course.Name,
        MarkedAt = c.MarkedAt,
    };
}
