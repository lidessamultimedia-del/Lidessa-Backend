namespace Lidessa.Api.Services.Notifications;

// Punto único para enviar avisos a usuarios desde cualquier parte del
// sistema. Los servicios dependen de esta interfaz, nunca de un canal
// concreto: hoy está registrada ConsoleNotificationService y, cuando haya
// SMTP/proveedor de correo, basta con registrar otra implementación en
// Program.cs sin tocar a quienes la usan.
public interface INotificationService
{
    // Devuelve true si el aviso quedó entregado al canal. Las
    // implementaciones no deben lanzar excepción por fallos de entrega
    // (destinatario inválido, proveedor caído): devuelven false y lo
    // registran, para que un aviso fallido no tumbe la operación de negocio
    // que ya se guardó.
    Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}
