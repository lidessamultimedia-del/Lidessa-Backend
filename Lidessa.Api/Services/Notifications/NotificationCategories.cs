namespace Lidessa.Api.Services.Notifications;

// Tipos de aviso que emite el sistema. Al agregar un aviso nuevo, se suma
// aquí su categoría en vez de usar strings sueltos.
public static class NotificationCategories
{
    public const string PqrsfReceived = "pqrsf.received";
    public const string PqrsfStatusChanged = "pqrsf.status-changed";
    public const string PqrsfResponded = "pqrsf.responded";
}
