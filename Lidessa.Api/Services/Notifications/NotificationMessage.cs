namespace Lidessa.Api.Services.Notifications;

// Un aviso dirigido a una persona, independiente del canal por el que salga
// (correo, consola, más adelante push/SMS). Lo arma quien dispara el aviso
// (PQRSF, recuperación de contraseña, calificaciones, etc.) y lo entrega la
// implementación de INotificationService que esté registrada.
public class NotificationMessage
{
    // Correo del destinatario. Puede quedar vacío si solo hay cuenta
    // (RecipientUserId) y el canal no necesita correo.
    public string RecipientEmail { get; init; } = string.Empty;

    public string RecipientName { get; init; } = string.Empty;

    // Cuenta del destinatario en AppUser, si la tiene.
    public long? RecipientUserId { get; init; }

    public string Subject { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    // Identificador estable del tipo de aviso (ver NotificationCategories),
    // útil para plantillas, filtros y logs.
    public string Category { get; init; } = string.Empty;

    public bool HasRecipient =>
        !string.IsNullOrWhiteSpace(RecipientEmail) || RecipientUserId is not null;
}
