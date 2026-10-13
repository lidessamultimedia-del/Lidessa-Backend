using System.ComponentModel.DataAnnotations;
using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Pqrsf;
using Lidessa.Api.Models;
using Lidessa.Api.Services.Notifications;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

// Flujo de estados de un PQRSF:
//
//   Pendiente ⇄ Revisando ──(responder)──► Respondida (final)
//        └───────────(responder)─────────────┘
//
// - Pendiente/Revisando se cambian libremente con ChangeStatusAsync.
// - Respondida solo se alcanza con RespondAsync (exige texto de respuesta) y
//   ya no se mueve de ahí. Sí se puede corregir la respuesta: se vuelve a
//   llamar RespondAsync y se le avisa de nuevo al solicitante.
// - Un ticket anónimo (sin cuenta ni correo) no se puede responder porque no
//   hay a quién: el admin solo puede marcarlo como Revisando/Pendiente.
public class PqrsfTicketService
{
    public static class Statuses
    {
        public const string Pending = "Pendiente";
        public const string Reviewing = "Revisando";
        public const string Responded = "Respondida";
    }

    // Deben coincidir con CK_PqrsfTicket_Type.
    public static readonly string[] TicketTypes =
        { "Petición", "Solicitud", "Queja", "Reclamo", "Sugerencia", "Felicitación" };

    private const string AnonymousName = "Anónimo";
    private const int MaxCodeAttempts = 3;

    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;

    public PqrsfTicketService(AppDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<bool> ExistsAsync(long id)
    {
        return await _db.PqrsfTickets.AnyAsync(t => t.Id == id);
    }

    public async Task<List<PqrsfTicketResponse>> GetAllAsync(string? status)
    {
        var query = _db.PqrsfTickets.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(t => t.Status == status);
        }

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Select(t => ToResponse(t))
            .ToListAsync();
    }

    public async Task<List<PqrsfTicketResponse>> GetByAccountAsync(long accountId)
    {
        return await _db.PqrsfTickets
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Select(t => ToResponse(t))
            .ToListAsync();
    }

    public async Task<PqrsfTicketResponse?> GetByIdAsync(long id)
    {
        var entity = await _db.PqrsfTickets.FindAsync(id);
        return entity is null ? null : ToResponse(entity);
    }

    // `accountId` es la cuenta con sesión iniciada, si la hay. En ese caso el
    // ticket queda ligado a ella y los datos de contacto salen de la cuenta,
    // no del formulario (mismo criterio que addTicket() del frontend).
    public async Task<(PqrsfTicketResponse? Result, string? Error)> CreateAsync(PqrsfCreateRequest request, long? accountId)
    {
        if (!TicketTypes.Contains(request.TicketType))
        {
            return (null, $"Tipo inválido. Use uno de: {string.Join(", ", TicketTypes)}");
        }

        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Message))
        {
            return (null, "El asunto y el mensaje son obligatorios");
        }

        var entity = new PqrsfTicket
        {
            TicketType = request.TicketType,
            Subject = request.Subject.Trim(),
            MessageBody = request.Message.Trim(),
            TicketDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = Statuses.Pending,
            CreatedAt = DateTime.UtcNow,
        };

        var account = accountId is null ? null : await _db.Users.FindAsync(accountId.Value);
        if (account is not null)
        {
            entity.AccountId = account.Id;
            entity.FromName = account.Name;
            entity.Email = account.Email;
            entity.Phone = account.Phone;
        }
        else
        {
            var email = request.Email?.Trim() ?? string.Empty;
            if (email.Length > 0 && !new EmailAddressAttribute().IsValid(email))
            {
                return (null, "Ingrese un correo válido");
            }

            entity.FromName = string.IsNullOrWhiteSpace(request.Name) ? AnonymousName : request.Name.Trim();
            entity.Email = email;
            entity.Phone = request.Phone?.Trim() ?? string.Empty;
        }

        _db.PqrsfTickets.Add(entity);
        await SaveWithUniqueCodeAsync(entity);

        await NotifyRequesterAsync(
            entity,
            NotificationCategories.PqrsfReceived,
            $"Recibimos su {entity.TicketType} — radicado {entity.TicketCode}",
            $"Hola {entity.FromName},\n\n" +
            $"Su {entity.TicketType} \"{entity.Subject}\" quedó registrada con el radicado {entity.TicketCode}. " +
            "Le daremos respuesta en un plazo máximo de 15 días hábiles conforme a la normativa vigente.");

        return (ToResponse(entity), null);
    }

    public async Task<(PqrsfTicketResponse? Result, string? Error)> ChangeStatusAsync(long id, string status)
    {
        var entity = await _db.PqrsfTickets.FindAsync(id);
        if (entity is null)
        {
            return (null, "PQRSF no encontrado");
        }

        if (status == Statuses.Responded)
        {
            return (null, "Para marcarlo como Respondida, envíe la respuesta");
        }

        if (status != Statuses.Pending && status != Statuses.Reviewing)
        {
            return (null, $"Estado inválido. Use '{Statuses.Pending}' o '{Statuses.Reviewing}'");
        }

        if (entity.Status == Statuses.Responded)
        {
            return (null, "Este PQRSF ya fue respondido y no puede cambiar de estado");
        }

        if (entity.Status == status)
        {
            return (ToResponse(entity), null);
        }

        entity.Status = status;
        entity.IsRead = true;
        await _db.SaveChangesAsync();

        if (status == Statuses.Reviewing)
        {
            await NotifyRequesterAsync(
                entity,
                NotificationCategories.PqrsfStatusChanged,
                $"Su {entity.TicketType} {entity.TicketCode} está en revisión",
                $"Hola {entity.FromName},\n\n" +
                $"Nuestro equipo ya está revisando su {entity.TicketType} \"{entity.Subject}\" " +
                $"(radicado {entity.TicketCode}). Le avisaremos cuando tengamos una respuesta.");
        }

        return (ToResponse(entity), null);
    }

    public async Task<(PqrsfTicketResponse? Result, string? Error)> RespondAsync(long id, string response)
    {
        var entity = await _db.PqrsfTickets.FindAsync(id);
        if (entity is null)
        {
            return (null, "PQRSF no encontrado");
        }

        if (!IsIdentified(entity))
        {
            return (null, "Este PQRSF es anónimo: no dejó cuenta ni correo, así que no hay a quién responderle");
        }

        if (string.IsNullOrWhiteSpace(response))
        {
            return (null, "La respuesta no puede estar vacía");
        }

        var isCorrection = entity.Status == Statuses.Responded;
        entity.Response = response.Trim();
        entity.RespondedAt = DateTime.UtcNow;
        entity.Status = Statuses.Responded;
        entity.IsRead = true;
        await _db.SaveChangesAsync();

        // La respuesta queda guardada aunque el aviso falle; EmailSent refleja
        // si de verdad salió, para que el admin lo vea en el panel.
        entity.EmailSent = await NotifyRequesterAsync(
            entity,
            NotificationCategories.PqrsfResponded,
            isCorrection
                ? $"Respuesta actualizada a su {entity.TicketType} — radicado {entity.TicketCode}"
                : $"Respuesta a su {entity.TicketType} — radicado {entity.TicketCode}",
            $"Hola {entity.FromName},\n\n" +
            $"Respecto a su {entity.TicketType} \"{entity.Subject}\" (radicado {entity.TicketCode}):\n\n" +
            entity.Response);
        await _db.SaveChangesAsync();

        return (ToResponse(entity), null);
    }

    public async Task<bool> MarkReadAsync(long id)
    {
        var entity = await _db.PqrsfTickets.FindAsync(id);
        if (entity is null)
        {
            return false;
        }

        if (!entity.IsRead)
        {
            entity.IsRead = true;
            await _db.SaveChangesAsync();
        }

        return true;
    }

    private static bool IsIdentified(PqrsfTicket t) =>
        t.AccountId is not null || !string.IsNullOrWhiteSpace(t.Email);

    private async Task<bool> NotifyRequesterAsync(PqrsfTicket ticket, string category, string subject, string body)
    {
        if (!IsIdentified(ticket))
        {
            return false;
        }

        return await _notifications.SendAsync(new NotificationMessage
        {
            RecipientEmail = ticket.Email,
            RecipientName = ticket.FromName,
            RecipientUserId = ticket.AccountId,
            Subject = subject,
            Body = body,
            Category = category,
        });
    }

    // Radicados consecutivos por año: PQRSF-2026-0001, PQRSF-2026-0002, ...
    // Si dos envíos simultáneos calculan el mismo número, el índice único de
    // TicketCode rechaza uno y se reintenta con el siguiente.
    private async Task SaveWithUniqueCodeAsync(PqrsfTicket entity)
    {
        for (var attempt = 1; ; attempt++)
        {
            entity.TicketCode = await NextTicketCodeAsync(entity.TicketDate.Year);
            try
            {
                await _db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException ex) when (attempt < MaxCodeAttempts && IsUniqueViolation(ex))
            {
                // Otro ticket se quedó con ese número; se recalcula.
            }
        }
    }

    private async Task<string> NextTicketCodeAsync(int year)
    {
        var prefix = $"PQRSF-{year}-";
        var last = await _db.PqrsfTickets
            .Where(t => t.TicketCode.StartsWith(prefix))
            .OrderByDescending(t => t.TicketCode.Length)
            .ThenByDescending(t => t.TicketCode)
            .Select(t => t.TicketCode)
            .FirstOrDefaultAsync();

        var next = last is not null && int.TryParse(last[prefix.Length..], out var n) ? n + 1 : 1;
        return $"{prefix}{next:D4}";
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    private static PqrsfTicketResponse ToResponse(PqrsfTicket t) => new()
    {
        Id = t.Id,
        TicketCode = t.TicketCode,
        TicketType = t.TicketType,
        FromName = t.FromName,
        Email = t.Email,
        Phone = t.Phone,
        Subject = t.Subject,
        MessageBody = t.MessageBody,
        TicketDate = t.TicketDate,
        Status = t.Status,
        Response = t.Response,
        RespondedAt = t.RespondedAt,
        EmailSent = t.EmailSent,
        AccountId = t.AccountId,
        IsRead = t.IsRead,
        CreatedAt = t.CreatedAt,
        IsIdentified = t.AccountId != null || t.Email != "",
    };
}
