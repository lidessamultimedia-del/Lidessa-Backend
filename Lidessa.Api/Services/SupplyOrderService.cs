using System.Security.Cryptography;
using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Supplies;
using Lidessa.Api.Models;
using Lidessa.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lidessa.Api.Services;

// Pedidos de la tienda de Suministros — calcado de createOrder() /
// confirmOrder() / rejectOrder() / shipOrder() / requestPaymentShortfall() /
// completeOrder() en SuppliesContext. Ciclo de vida:
//   pending ──confirm──▶ confirmed ──ship──▶ shipped ──complete──▶ completed
//      └────reject────▶ rejected
// El aviso de pago incompleto no es un estado: se anota sobre un pedido
// pendiente, que sigue pendiente.
public class SupplyOrderService
{
    // El cliente puede tardar días o semanas en recibir el pedido y hacer
    // clic en el correo — más largo que el token de invitación de cuenta
    // (7 días). Valor elegido por mí, a falta de uno explícito en el plan;
    // ajustable si Cristian/Santiago prefieren otro.
    private static readonly TimeSpan DeliveryTokenLifetime = TimeSpan.FromDays(30);

    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ApiOptions _apiOptions;

    public SupplyOrderService(AppDbContext db, INotificationService notifications, IOptions<ApiOptions> apiOptions)
    {
        _db = db;
        _notifications = notifications;
        _apiOptions = apiOptions.Value;
    }

    public async Task<List<SupplyOrderResponse>> GetAllAsync()
    {
        return await Ordered(_db.SupplyOrders).Select(o => ToResponse(o)).ToListAsync();
    }

    public async Task<List<SupplyOrderResponse>> GetMineAsync(long clientId)
    {
        return await Ordered(_db.SupplyOrders.Where(o => o.ClientId == clientId))
            .Select(o => ToResponse(o))
            .ToListAsync();
    }

    // El admin ve cualquier pedido; un cliente solo el suyo (null si no le
    // pertenece, igual que si no existiera).
    public async Task<SupplyOrderResponse?> GetByIdAsync(long id, long? callerClientId)
    {
        var entity = await WithDetails(_db.SupplyOrders).FirstOrDefaultAsync(o => o.Id == id);
        if (entity is null)
        {
            return null;
        }

        if (callerClientId is not null && entity.ClientId != callerClientId)
        {
            return null;
        }

        return ToResponse(entity);
    }

    // callerClientId: si quien llama tiene rol "cliente", el pedido queda a
    // su nombre sin importar lo que mande en request.ClientId (igual que
    // ClientStore, que siempre pasa clientId: user.id). Si es null (admin),
    // se respeta request.ClientId — puede quedar sin cuenta ("Cliente sin
    // cuenta" en OrderFormModal).
    public async Task<(SupplyOrderResponse? Result, string? Error)> CreateAsync(SupplyOrderRequest request, long? callerClientId)
    {
        if (request.Items.Count == 0)
        {
            return (null, "Agregue al menos un producto");
        }

        var clientId = callerClientId ?? request.ClientId;
        if (clientId is not null && !await _db.Users.AnyAsync(u => u.Id == clientId))
        {
            return (null, "Cliente no encontrado");
        }

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.SupplyProducts.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        var quantityByProduct = new Dictionary<long, int>();
        foreach (var item in request.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                return (null, "Uno de los productos ya no existe");
            }

            if (!product.Active)
            {
                return (null, $"\"{product.Name}\" ya no está disponible");
            }

            quantityByProduct[item.ProductId] = quantityByProduct.GetValueOrDefault(item.ProductId) + item.Quantity;
        }

        foreach (var (productId, quantity) in quantityByProduct)
        {
            var product = products[productId];
            if (quantity > product.Stock)
            {
                return (null, $"No hay suficiente stock de \"{product.Name}\" (disponible: {product.Stock})");
            }
        }

        var now = DateTime.UtcNow;
        var entity = new SupplyOrder
        {
            ClientId = clientId,
            CustomerName = request.CustomerName.Trim(),
            CustomerPhone = request.CustomerPhone.Trim(),
            CustomerEmail = request.CustomerEmail?.Trim() ?? string.Empty,
            Address = request.Address.Trim(),
            City = request.City?.Trim() ?? string.Empty,
            Notes = request.Notes?.Trim() ?? string.Empty,
            ProofImageUrl = string.IsNullOrWhiteSpace(request.ProofImageUrl) ? null : request.ProofImageUrl.Trim(),
            Status = SupplyOrderStatuses.Pending,
            StatusUpdatedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Nombre y precio se copian del catálogo al momento de comprar (no
        // de lo que mande el cliente): así el pedido no cambia si después se
        // edita o se borra el producto, y nadie puede mandar un precio
        // distinto al real.
        entity.Items = request.Items.Select(item =>
        {
            var product = products[item.ProductId];
            return new SupplyOrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = item.Quantity,
            };
        }).ToList();
        entity.Total = entity.Items.Sum(i => i.UnitPrice * i.Quantity);
        entity.StatusHistory.Add(new SupplyOrderStatusChange { Status = SupplyOrderStatuses.Pending, ChangedAt = now });

        // Aparta el stock al crear el pedido (no al confirmarlo): así nadie
        // más puede seguir comprando unidades ya apartadas mientras el
        // comprobante está pendiente de revisión.
        foreach (var (productId, quantity) in quantityByProduct)
        {
            products[productId].Stock -= quantity;
        }

        _db.SupplyOrders.Add(entity);
        await _db.SaveChangesAsync();

        return (await GetByIdAsync(entity.Id, null), null);
    }

    public async Task<(SupplyOrderResponse? Result, string? Error)> ConfirmAsync(long id)
    {
        var entity = await _db.SupplyOrders.FindAsync(id);
        if (entity is null)
        {
            return (null, "Pedido no encontrado");
        }

        if (entity.Status != SupplyOrderStatuses.Pending)
        {
            return (null, "Solo se puede confirmar un pedido pendiente de revisión");
        }

        var now = DateTime.UtcNow;
        entity.Status = SupplyOrderStatuses.Confirmed;
        entity.StatusUpdatedAt = now;
        entity.UpdatedAt = now;
        _db.SupplyOrderStatusChanges.Add(new SupplyOrderStatusChange { OrderId = id, Status = SupplyOrderStatuses.Confirmed, ChangedAt = now });

        await _db.SaveChangesAsync();

        await NotifyCustomerAsync(entity, NotificationCategories.SuppliesOrderConfirmed,
            $"Tu pedido #{entity.Id} fue confirmado",
            $"Hola {entity.CustomerName},\n\nTu pedido #{entity.Id} por {entity.Total:N0} COP ya fue confirmado. Te avisaremos cuando salga despachado.");

        return (await GetByIdAsync(id, null), null);
    }

    public async Task<(SupplyOrderResponse? Result, string? Error)> RejectAsync(long id, RejectSupplyOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return (null, "Escriba la causa del rechazo");
        }

        var entity = await _db.SupplyOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
        if (entity is null)
        {
            return (null, "Pedido no encontrado");
        }

        if (entity.Status != SupplyOrderStatuses.Pending)
        {
            return (null, "Solo se puede rechazar un pedido pendiente de revisión");
        }

        // Devuelve al inventario el stock que este pedido había apartado.
        var productIds = entity.Items.Where(i => i.ProductId is not null).Select(i => i.ProductId!.Value).Distinct().ToList();
        var products = await _db.SupplyProducts.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        foreach (var item in entity.Items)
        {
            if (item.ProductId is not null && products.TryGetValue(item.ProductId.Value, out var product))
            {
                product.Stock += item.Quantity;
            }
        }

        var now = DateTime.UtcNow;
        entity.Status = SupplyOrderStatuses.Rejected;
        entity.StatusUpdatedAt = now;
        entity.UpdatedAt = now;
        entity.RejectionReason = request.Reason.Trim();
        entity.RefundProofUrl = string.IsNullOrWhiteSpace(request.RefundProofUrl) ? null : request.RefundProofUrl.Trim();
        _db.SupplyOrderStatusChanges.Add(new SupplyOrderStatusChange { OrderId = id, Status = SupplyOrderStatuses.Rejected, ChangedAt = now });

        await _db.SaveChangesAsync();

        await NotifyCustomerAsync(entity, NotificationCategories.SuppliesOrderRejected,
            $"Tu pedido #{entity.Id} fue rechazado",
            $"Hola {entity.CustomerName},\n\nTu pedido #{entity.Id} fue rechazado. Causa: \"{entity.RejectionReason}\".");

        return (await GetByIdAsync(id, null), null);
    }

    // El pedido confirmado ya se empacó y despachó: queda la transportadora,
    // la guía y la foto del paquete para que el cliente le haga seguimiento.
    // No toca el stock: ya se descontó al crear el pedido y, desde aquí, ya
    // salió del inventario (deja de estar en HoldingStock).
    public async Task<(SupplyOrderResponse? Result, string? Error)> ShipAsync(long id, ShipSupplyOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Carrier))
        {
            return (null, "Indica la transportadora");
        }

        if (string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            return (null, "El número de guía es obligatorio");
        }

        if (string.IsNullOrWhiteSpace(request.PhotoUrl))
        {
            return (null, "Sube una foto del paquete");
        }

        var entity = await _db.SupplyOrders.FindAsync(id);
        if (entity is null)
        {
            return (null, "Pedido no encontrado");
        }

        if (entity.Status != SupplyOrderStatuses.Confirmed)
        {
            return (null, "Solo se puede despachar un pedido confirmado");
        }

        var now = DateTime.UtcNow;
        entity.ShipmentCarrier = request.Carrier.Trim();
        entity.ShipmentTrackingNumber = request.TrackingNumber.Trim();
        entity.ShipmentPhotoUrl = request.PhotoUrl.Trim();
        entity.ShippedAt = now;
        SetStatus(entity, SupplyOrderStatuses.Shipped, now);

        // Token de un solo uso para el botón "Ya me llegó" del correo — se
        // genera uno nuevo en cada despacho (no debería repetirse, pero un
        // pedido solo se despacha una vez en la práctica).
        var deliveryToken = GenerateDeliveryToken();
        _db.SupplyOrderDeliveryTokens.Add(new SupplyOrderDeliveryToken
        {
            OrderId = id,
            Token = deliveryToken,
            ExpiresAt = now.Add(DeliveryTokenLifetime),
            CreatedAt = now,
        });

        await _db.SaveChangesAsync();

        var confirmLink = $"{_apiOptions.BaseUrl}/api/supplies/orders/confirm-delivery/{deliveryToken}";
        await NotifyCustomerAsync(entity, NotificationCategories.SuppliesOrderShipped,
            $"Tu pedido #{entity.Id} fue enviado",
            $"Hola {entity.CustomerName},\n\nTu pedido #{entity.Id} ya fue despachado con {entity.ShipmentCarrier}, guía {entity.ShipmentTrackingNumber}.\n\nCuando te llegue, confirma la entrega en este enlace: {confirmLink}");

        return (await GetByIdAsync(id, null), null);
    }

    // El comprobante no cubre el total: se le anota al cliente cuánto falta
    // y por qué. El pedido sigue pendiente (no se agrega al historial) y un
    // aviso nuevo reemplaza al anterior, igual que en el frontend.
    public async Task<(SupplyOrderResponse? Result, string? Error)> RequestPaymentShortfallAsync(long id, PaymentShortfallRequest request)
    {
        // request.Amount > 0 ya lo garantiza el [Range] del DTO (400
        // automático de [ApiController] antes de llegar aquí).
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return (null, "Escribe un mensaje para el cliente");
        }

        var entity = await _db.SupplyOrders.FindAsync(id);
        if (entity is null)
        {
            return (null, "Pedido no encontrado");
        }

        if (entity.Status != SupplyOrderStatuses.Pending)
        {
            return (null, "Solo se puede avisar pago incompleto en un pedido pendiente de revisión");
        }

        if (request.Amount > entity.Total)
        {
            return (null, "El monto que falta no puede ser mayor que el total del pedido");
        }

        var now = DateTime.UtcNow;
        entity.PaymentNoteAmount = request.Amount;
        entity.PaymentNoteMessage = request.Message.Trim();
        entity.PaymentNoteDate = now;
        entity.UpdatedAt = now;

        await _db.SaveChangesAsync();

        await NotifyCustomerAsync(entity, NotificationCategories.SuppliesPaymentShortfall,
            $"Falta completar el pago de tu pedido #{entity.Id}",
            $"Hola {entity.CustomerName},\n\nEl comprobante de tu pedido #{entity.Id} no cubre el total. Falta: {entity.PaymentNoteAmount:N0} COP.\n\n{entity.PaymentNoteMessage}");

        return (await GetByIdAsync(id, null), null);
    }

    // "Ya me llegó el pedido": lo marca el cliente desde su cuenta. El admin
    // también puede (callerClientId null) para cerrar pedidos walk-in sin
    // cuenta, que de otro modo se quedarían en "enviado" para siempre. Un
    // cliente que no es dueño del pedido recibe "no encontrado".
    public async Task<(SupplyOrderResponse? Result, string? Error)> CompleteAsync(long id, long? callerClientId)
    {
        var entity = await _db.SupplyOrders.FindAsync(id);
        if (entity is null || (callerClientId is not null && entity.ClientId != callerClientId))
        {
            return (null, "Pedido no encontrado");
        }

        if (entity.Status != SupplyOrderStatuses.Shipped)
        {
            return (null, "Solo se puede marcar como recibido un pedido enviado");
        }

        SetStatus(entity, SupplyOrderStatuses.Completed, DateTime.UtcNow);

        await _db.SaveChangesAsync();

        return (await GetByIdAsync(id, null), null);
    }

    // Botón "Ya me llegó" del correo de envío: ruta pública (sin login),
    // protegida por el token de un solo uso que ShipAsync generó. Devuelve
    // solo éxito/error (no el pedido completo) porque cualquiera con el
    // enlace llega aquí sin haber iniciado sesión.
    public async Task<(bool Success, string? Error)> ConfirmDeliveryAsync(string token)
    {
        var deliveryToken = await _db.SupplyOrderDeliveryTokens
            .Include(t => t.Order)
            .FirstOrDefaultAsync(t => t.Token == token && t.UsedAt == null);

        if (deliveryToken is null || deliveryToken.ExpiresAt <= DateTime.UtcNow)
        {
            return (false, "El enlace no es válido o ya expiró");
        }

        var entity = deliveryToken.Order;
        if (entity.Status != SupplyOrderStatuses.Shipped)
        {
            return (false, "Este pedido ya no está en estado \"enviado\"");
        }

        var now = DateTime.UtcNow;
        deliveryToken.UsedAt = now;
        SetStatus(entity, SupplyOrderStatuses.Completed, now);

        await _db.SaveChangesAsync();

        // A quien ya hizo clic no hace falta avisarle por correo (acaba de
        // confirmarlo él mismo) — el aviso es para la administradora, para
        // que quede registrado que el cliente ya recibió el pedido.
        var admin = await _db.Users.AsNoTracking().Where(u => u.Role == "admin").OrderBy(u => u.Id).FirstOrDefaultAsync();
        if (admin is not null)
        {
            await _notifications.SendAsync(new NotificationMessage
            {
                RecipientEmail = admin.Email,
                RecipientName = admin.Name,
                RecipientUserId = admin.Id,
                Subject = $"Pedido #{entity.Id} confirmado como entregado",
                Body = $"{entity.CustomerName} confirmó que ya recibió el pedido #{entity.Id} ({entity.Total:N0} COP).",
                Category = NotificationCategories.SuppliesDeliveryConfirmed,
            });
        }

        return (true, null);
    }

    private Task NotifyCustomerAsync(SupplyOrder entity, string category, string subject, string body) =>
        _notifications.SendAsync(new NotificationMessage
        {
            RecipientEmail = entity.CustomerEmail,
            RecipientName = entity.CustomerName,
            RecipientUserId = entity.ClientId,
            Subject = subject,
            Body = body,
            Category = category,
        });

    private static string GenerateDeliveryToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    // pushStatus() del frontend: cambia el estado y deja la entrada en el
    // historial con la misma fecha.
    private void SetStatus(SupplyOrder entity, string status, DateTime now)
    {
        entity.Status = status;
        entity.StatusUpdatedAt = now;
        entity.UpdatedAt = now;
        _db.SupplyOrderStatusChanges.Add(new SupplyOrderStatusChange { OrderId = entity.Id, Status = status, ChangedAt = now });
    }

    private static IQueryable<SupplyOrder> Ordered(IQueryable<SupplyOrder> query) =>
        WithDetails(query).OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id);

    private static IQueryable<SupplyOrder> WithDetails(IQueryable<SupplyOrder> query) =>
        query.AsNoTracking().Include(o => o.Items).Include(o => o.StatusHistory);

    private static SupplyOrderResponse ToResponse(SupplyOrder o) => new()
    {
        Id = o.Id,
        ClientId = o.ClientId,
        CustomerName = o.CustomerName,
        CustomerPhone = o.CustomerPhone,
        CustomerEmail = o.CustomerEmail,
        Address = o.Address,
        City = o.City,
        Notes = o.Notes,
        Total = o.Total,
        ProofImageUrl = o.ProofImageUrl,
        Status = o.Status,
        StatusUpdatedAt = o.StatusUpdatedAt,
        PaymentNote = o.PaymentNoteAmount is null || o.PaymentNoteDate is null
            ? null
            : new SupplyOrderPaymentNoteResponse
            {
                Amount = o.PaymentNoteAmount.Value,
                Message = o.PaymentNoteMessage ?? string.Empty,
                Date = o.PaymentNoteDate.Value,
            },
        Shipment = o.ShippedAt is null
            ? null
            : new SupplyOrderShipmentResponse
            {
                Carrier = o.ShipmentCarrier ?? string.Empty,
                TrackingNumber = o.ShipmentTrackingNumber ?? string.Empty,
                PhotoUrl = o.ShipmentPhotoUrl,
                Date = o.ShippedAt.Value,
            },
        RejectionReason = o.RejectionReason,
        RefundProofUrl = o.RefundProofUrl,
        CreatedAt = o.CreatedAt,
        UpdatedAt = o.UpdatedAt,
        Items = o.Items.Select(i => new SupplyOrderItemResponse
        {
            Id = i.Id,
            ProductId = i.ProductId,
            ProductName = i.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Quantity,
        }).ToList(),
        StatusHistory = o.StatusHistory
            .OrderBy(h => h.ChangedAt).ThenBy(h => h.Id)
            .Select(h => new SupplyOrderStatusChangeResponse { Status = h.Status, ChangedAt = h.ChangedAt })
            .ToList(),
    };
}
