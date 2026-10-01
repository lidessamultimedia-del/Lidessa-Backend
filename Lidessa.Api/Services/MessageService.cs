using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Messages;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

// Mensajería estudiante <-> profesor/admin, por curso — calcado de
// sendMessage/studentConversations/staffConversations en LMSContext. El
// frontend hoy no valida quién le puede escribir a quién (confía en que la UI
// solo ofrece las contrapartes correctas); aquí sí se exige porque es la API.
public class MessageService
{
    private readonly AppDbContext _db;

    public MessageService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> CourseExistsAsync(long courseId)
    {
        return await _db.Courses.AnyAsync(c => c.Id == courseId);
    }

    // Quién puede participar en la mensajería de un curso: el admin siempre,
    // el profesor dueño, o un estudiante inscrito.
    public async Task<bool> CanParticipateAsync(long courseId, long userId, string role)
    {
        if (role == "admin")
        {
            return true;
        }

        if (role == "profesor")
        {
            return await _db.Courses.AnyAsync(c => c.Id == courseId && c.TeacherId == userId);
        }

        return await _db.CourseEnrollments.AnyAsync(e => e.CourseId == courseId && e.StudentId == userId);
    }

    public async Task<(MessageResponse? Result, string? Error)> SendAsync(long courseId, long fromUserId, string fromRole, SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return (null, "El mensaje no puede estar vacío");
        }

        if (request.ToUserId == fromUserId)
        {
            return (null, "No puedes enviarte un mensaje a ti mismo");
        }

        var toUserExists = await _db.Users.AnyAsync(u => u.Id == request.ToUserId);
        if (!toUserExists)
        {
            return (null, "El destinatario no existe");
        }

        var error = await ValidateRecipientAsync(courseId, fromUserId, fromRole, request.ToUserId);
        if (error is not null)
        {
            return (null, error);
        }

        var entity = new Message
        {
            CourseId = courseId,
            FromUserId = fromUserId,
            ToUserId = request.ToUserId,
            Body = request.Body.Trim(),
        };

        _db.Messages.Add(entity);
        await _db.SaveChangesAsync();

        return (ToResponse(entity), null);
    }

    // Estudiante: solo al profesor del curso o a un admin — igual que
    // `parties = [course.teacherId, adminId]` en studentConversations.
    // Profesor: solo a un estudiante inscrito en su propio curso.
    // Admin: al profesor del curso o a un estudiante inscrito (actúa como
    // staff de cualquier curso, igual que staffConversations no lo restringe
    // a "sus" cursos).
    private async Task<string?> ValidateRecipientAsync(long courseId, long fromUserId, string fromRole, long toUserId)
    {
        var teacherId = await _db.Courses.Where(c => c.Id == courseId).Select(c => c.TeacherId).SingleAsync();
        var toIsEnrolledStudent = await _db.CourseEnrollments.AnyAsync(e => e.CourseId == courseId && e.StudentId == toUserId);

        if (fromRole == "estudiante")
        {
            if (toUserId == teacherId)
            {
                return null;
            }

            var toIsAdmin = await _db.Users.AnyAsync(u => u.Id == toUserId && u.Role == "admin");
            return toIsAdmin ? null : "Solo puedes escribirle al profesor del curso o a un administrador";
        }

        if (fromRole == "admin")
        {
            return toUserId == teacherId || toIsEnrolledStudent
                ? null
                : "El destinatario no tiene relación con este curso";
        }

        // profesor (ya se validó que es dueño del curso en CanParticipateAsync)
        return toIsEnrolledStudent ? null : "El destinatario no está inscrito en este curso";
    }

    private static MessageResponse ToResponse(Message m) => new()
    {
        Id = m.Id,
        CourseId = m.CourseId,
        FromUserId = m.FromUserId,
        ToUserId = m.ToUserId,
        Body = m.Body,
        CreatedAt = m.CreatedAt,
        IsRead = m.IsRead,
    };
}
