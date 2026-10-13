namespace Lidessa.Api.Services.Notifications;

// Implementación de desarrollo: en vez de enviar el correo, lo escribe en la
// consola del API (vía ILogger) con todo su contenido. Sirve para probar los
// flujos completos sin servidor de correo.
public class ConsoleNotificationService : INotificationService
{
    private readonly ILogger<ConsoleNotificationService> _logger;

    public ConsoleNotificationService(ILogger<ConsoleNotificationService> logger)
    {
        _logger = logger;
    }

    public Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (!message.HasRecipient)
        {
            _logger.LogWarning(
                "[Notificación:{Category}] Descartada: no tiene destinatario (asunto: {Subject})",
                message.Category, message.Subject);
            return Task.FromResult(false);
        }

        _logger.LogInformation(
            "[Notificación:{Category}] Para: {Name} <{Email}> (cuenta: {UserId})\nAsunto: {Subject}\n{Body}",
            message.Category,
            message.RecipientName,
            message.RecipientEmail,
            message.RecipientUserId?.ToString() ?? "sin cuenta",
            message.Subject,
            message.Body);

        return Task.FromResult(true);
    }
}
