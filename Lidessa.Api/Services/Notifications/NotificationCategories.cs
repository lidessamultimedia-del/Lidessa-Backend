namespace Lidessa.Api.Services.Notifications;

// Tipos de aviso que emite el sistema. Al agregar un aviso nuevo, se suma
// aquí su categoría en vez de usar strings sueltos.
public static class NotificationCategories
{
    public const string PqrsfReceived = "pqrsf.received";
    public const string PqrsfStatusChanged = "pqrsf.status-changed";
    public const string PqrsfResponded = "pqrsf.responded";
    public const string AccountInvite = "account.invite";

    // Suministros (ver hallazgo 2.1 del plan): cuenta creada, pedido
    // confirmado/rechazado/enviado, pago incompleto y confirmación de
    // entrega — las 6 categorías nuevas de este módulo.
    public const string SuppliesAccountCreated = "supplies.account-created";
    public const string SuppliesOrderConfirmed = "supplies.order-confirmed";
    public const string SuppliesOrderRejected = "supplies.order-rejected";
    public const string SuppliesOrderShipped = "supplies.order-shipped";
    public const string SuppliesPaymentShortfall = "supplies.payment-shortfall";
    public const string SuppliesDeliveryConfirmed = "supplies.delivery-confirmed";
}
