using System.Collections.Concurrent;
using Lidessa.Api.Services.Notifications;

namespace Lidessa.Api.Tests.Infrastructure;

// Reemplaza a INotificationService en LmsApiFactory para poder verificar,
// en las pruebas, qué aviso (categoría, destinatario, asunto) disparó cada
// acción — sin necesidad de un servidor de correo real.
public class RecordingNotificationService : INotificationService
{
    private readonly ConcurrentBag<NotificationMessage> _sent;

    public RecordingNotificationService(ConcurrentBag<NotificationMessage> sent)
    {
        _sent = sent;
    }

    public Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Add(message);
        return Task.FromResult(true);
    }
}
