using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Enrollments;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class CourseEnrollmentService
{
    private readonly AppDbContext _db;

    public CourseEnrollmentService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<EnrollmentResponse>> GetAllByCourseAsync(long courseId)
    {
        return await _db.CourseEnrollments
            .Include(e => e.Student)
            .Where(e => e.CourseId == courseId)
            .OrderBy(e => e.Student.Name)
            .Select(e => ToResponse(e))
            .ToListAsync();
    }

    public async Task<(EnrollmentResponse? Result, string? Error)> EnrollAsync(long courseId, EnrollmentRequest request)
    {
        var course = await _db.Courses.FindAsync(courseId);
        if (course is null)
        {
            return (null, "Curso no encontrado");
        }

        var student = await _db.Users.SingleOrDefaultAsync(u => u.Id == request.StudentId);
        if (student is null || student.Role != "estudiante")
        {
            return (null, "El estudiante indicado no existe");
        }

        var alreadyEnrolled = await _db.CourseEnrollments.AnyAsync(e => e.CourseId == courseId && e.StudentId == request.StudentId);
        if (alreadyEnrolled)
        {
            return (null, "El estudiante ya está inscrito en este curso");
        }

        if (course.Capacity is not null)
        {
            var currentCount = await _db.CourseEnrollments.CountAsync(e => e.CourseId == courseId);
            if (currentCount >= course.Capacity)
            {
                return (null, "El curso ya alcanzó su capacidad máxima");
            }
        }

        var entity = new CourseEnrollment
        {
            CourseId = courseId,
            StudentId = request.StudentId,
            EnrolledAt = DateTime.UtcNow,
        };

        _db.CourseEnrollments.Add(entity);
        await _db.SaveChangesAsync();

        return (new EnrollmentResponse
        {
            CourseId = courseId,
            StudentId = student.Id,
            StudentName = student.Name,
            StudentEmail = student.Email,
            EnrolledAt = entity.EnrolledAt,
        }, null);
    }

    public async Task<bool> UnenrollAsync(long courseId, long studentId)
    {
        var entity = await _db.CourseEnrollments.SingleOrDefaultAsync(e => e.CourseId == courseId && e.StudentId == studentId);
        if (entity is null)
        {
            return false;
        }

        _db.CourseEnrollments.Remove(entity);
        await _db.SaveChangesAsync();

        return true;
    }

    private static EnrollmentResponse ToResponse(CourseEnrollment e) => new()
    {
        CourseId = e.CourseId,
        StudentId = e.StudentId,
        StudentName = e.Student.Name,
        StudentEmail = e.Student.Email,
        EnrolledAt = e.EnrolledAt,
    };
}
