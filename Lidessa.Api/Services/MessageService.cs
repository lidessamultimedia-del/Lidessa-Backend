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

    // Hilo entre el usuario actual y una contraparte dentro de un curso, en
    // orden cronológico — calcado de threadMessages. Solo devuelve mensajes en
    // los que participa `userId`, así que nadie puede leer hilos ajenos.
    public async Task<List<MessageResponse>> GetThreadAsync(long courseId, long userId, long otherUserId)
    {
        var messages = await _db.Messages
            .AsNoTracking()
            .Where(m => m.CourseId == courseId && (
                (m.FromUserId == userId && m.ToUserId == otherUserId) ||
                (m.FromUserId == otherUserId && m.ToUserId == userId)))
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync();

        return messages.Select(ToResponse).ToList();
    }

    // Marca como leídos los mensajes que `otherUserId` le envió a `readerId`
    // en el curso — calcado de markThreadRead. Devuelve cuántos se marcaron.
    public async Task<int> MarkThreadReadAsync(long courseId, long readerId, long otherUserId)
    {
        return await _db.Messages
            .Where(m => m.CourseId == courseId && m.ToUserId == readerId && m.FromUserId == otherUserId && !m.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsRead, true));
    }

    // Agrupa los mensajes de un miembro del staff (profesor o admin) en
    // conversaciones (una por curso + contraparte) — calcado de
    // staffConversations. No se restringe a "sus" cursos: se arma directo de
    // los mensajes en los que participó, así sirve igual para el profesor de
    // un curso que para el admin (que no es profesor de ninguno).
    public async Task<List<StaffConversationResponse>> GetStaffConversationsAsync(long staffId)
    {
        var relevant = await _db.Messages
            .AsNoTracking()
            .Where(m => m.FromUserId == staffId || m.ToUserId == staffId)
            .ToListAsync();

        return relevant
            .GroupBy(m => (m.CourseId, OtherUserId: m.FromUserId == staffId ? m.ToUserId : m.FromUserId))
            .Select(g =>
            {
                var last = g.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).First();
                var unread = g.Count(m => m.FromUserId == g.Key.OtherUserId && m.ToUserId == staffId && !m.IsRead);
                return new StaffConversationResponse
                {
                    CourseId = g.Key.CourseId,
                    OtherUserId = g.Key.OtherUserId,
                    LastMessage = ToResponse(last),
                    UnreadCount = unread,
                };
            })
            .OrderByDescending(c => c.LastMessage.CreatedAt)
            .ToList();
    }

    // Una conversación por curso inscrito, por cada persona con la que el
    // estudiante puede escribirse (su profesor y un admin) — calcado de
    // studentConversations, incluyendo a los que todavía no tienen ningún
    // mensaje, para que el estudiante siempre pueda iniciar la conversación.
    public async Task<List<StudentConversationResponse>> GetStudentConversationsAsync(long studentId)
    {
        var adminId = await _db.Users
            .Where(u => u.Role == "admin")
            .OrderBy(u => u.Id)
            .Select(u => (long?)u.Id)
            .FirstOrDefaultAsync();

        var courses = await _db.CourseEnrollments
            .Where(e => e.StudentId == studentId)
            .Select(e => new { e.Course.Id, e.Course.TeacherId })
            .ToListAsync();

        var results = new List<StudentConversationResponse>();

        foreach (var course in courses)
        {
            var parties = new List<long>();
            if (course.TeacherId is not null)
            {
                parties.Add(course.TeacherId.Value);
            }

            if (adminId is not null && !parties.Contains(adminId.Value))
            {
                parties.Add(adminId.Value);
            }

            foreach (var otherId in parties)
            {
                var thread = await _db.Messages
                    .AsNoTracking()
                    .Where(m => m.CourseId == course.Id && (
                        (m.FromUserId == studentId && m.ToUserId == otherId) ||
                        (m.FromUserId == otherId && m.ToUserId == studentId)))
                    .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                    .ToListAsync();

                var last = thread.FirstOrDefault();

                results.Add(new StudentConversationResponse
                {
                    CourseId = course.Id,
                    OtherUserId = otherId,
                    LastMessage = last is null ? null : ToResponse(last),
                    UnreadCount = thread.Count(m => m.ToUserId == studentId && !m.IsRead),
                });
            }
        }

        return results
            .OrderByDescending(c => c.UnreadCount)
            .ThenByDescending(c => c.LastMessage?.CreatedAt ?? DateTime.MinValue)
            .ToList();
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
