using System.Net;
using System.Net.Http.Json;
using Lidessa.Api.Dtos.Supplies;
using Lidessa.Api.Models;
using Lidessa.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Tests;

// Escenario: un admin, dos cuentas de cliente y dos productos activos con
// stock limitado (más uno oculto) para probar el apartado/devolución de
// inventario.
public class SupplyOrdersEndpointsTests : IAsyncLifetime
{
    private readonly LmsApiFactory _api = new();

    private static readonly AppUser Admin = new() { Id = 1, Name = "admin", Email = "admin@lidessa.test", Role = "admin" };
    private static readonly AppUser ClientA = new() { Id = 20, Name = "Constructora Andina", Email = "andina@lidessa.test", Role = "cliente" };
    private static readonly AppUser ClientB = new() { Id = 21, Name = "Textiles del Valle", Email = "textiles@lidessa.test", Role = "cliente" };

    private const long KitId = 1;
    private const long HelmetId = 2;
    private const long HiddenId = 3;

    public async Task InitializeAsync()
    {
        await _api.SeedAsync(db =>
        {
            db.Users.AddRange(Admin, ClientA, ClientB);
            db.SupplyProducts.AddRange(
                new SupplyProduct { Id = KitId, Name = "Botiquín tipo A", Description = "Desc", Price = 185000, Stock = 5, Active = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SupplyProduct { Id = HelmetId, Name = "Casco", Description = "Desc", Price = 65000, Stock = 3, Active = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SupplyProduct { Id = HiddenId, Name = "Oculto", Description = "Desc", Price = 1000, Stock = 10, Active = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            return Task.CompletedTask;
        });
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private HttpClient Anonymous() => _api.CreateClient();

    private HttpClient As(AppUser user) => _api.ClientFor(user);

    private static object OrderPayload(long productId = KitId, int quantity = 2, long? clientId = null) => new
    {
        clientId,
        customerName = "Constructora Andina",
        customerPhone = "300 123 4567",
        customerEmail = "andina@lidessa.test",
        address = "Calle 10 # 20-30",
        city = "Medellín",
        notes = "Entregar en portería",
        proofImageUrl = "/uploads/attachments/comprobante.jpg",
        items = new[] { new { productId, quantity } },
    };

    // ── Crear ──

    [Fact]
    public async Task Client_creates_order_resolves_price_from_catalog_and_holds_stock()
    {
        var response = await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Equal(ClientA.Id, order!.ClientId);
        Assert.Equal("pending", order.Status);
        Assert.Equal(370000m, order.Total); // 185000 * 2, calculado por el servidor
        Assert.Single(order.Items);
        Assert.Equal("Botiquín tipo A", order.Items[0].ProductName);
        Assert.Equal(185000m, order.Items[0].UnitPrice);
        Assert.Single(order.StatusHistory);
        Assert.Equal("pending", order.StatusHistory[0].Status);

        var stock = await _api.QueryAsync(db => db.SupplyProducts.Where(p => p.Id == KitId).Select(p => p.Stock).SingleAsync());
        Assert.Equal(3, stock); // 5 - 2
    }

    [Fact]
    public async Task Create_rejects_insufficient_stock()
    {
        var response = await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(HelmetId, 10));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(3, await _api.QueryAsync(db => db.SupplyProducts.Where(p => p.Id == HelmetId).Select(p => p.Stock).SingleAsync()));
    }

    [Fact]
    public async Task Create_rejects_inactive_product()
    {
        var response = await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(HiddenId, 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_unknown_product()
    {
        var response = await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(999, 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_requires_at_least_one_item()
    {
        var response = await As(ClientA).PostAsJsonAsync("/api/supplies/orders", new
        {
            customerName = "Constructora Andina",
            customerPhone = "300 123 4567",
            address = "Calle 10 # 20-30",
            items = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Client_cannot_assign_order_to_another_client()
    {
        var response = await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 1, clientId: ClientB.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Equal(ClientA.Id, order!.ClientId); // se ignora el clientId del body, no el de ClientB
    }

    [Fact]
    public async Task Admin_creates_walkin_order_without_client_account()
    {
        var response = await As(Admin).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 1, clientId: null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Null(order!.ClientId);
    }

    [Fact]
    public async Task Admin_creates_order_for_a_specific_client()
    {
        var response = await As(Admin).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 1, clientId: ClientB.Id));

        var order = await response.Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Equal(ClientB.Id, order!.ClientId);
    }

    [Fact]
    public async Task Anonymous_cannot_create_order()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/supplies/orders", OrderPayload());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Lectura ──

    [Fact]
    public async Task Client_sees_only_their_own_orders_in_mine()
    {
        await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 1));
        await As(ClientB).PostAsJsonAsync("/api/supplies/orders", OrderPayload(HelmetId, 1));

        var mine = await As(ClientA).GetFromJsonAsync<List<SupplyOrderResponse>>("/api/supplies/orders/mine");

        Assert.Single(mine!);
        Assert.Equal(ClientA.Id, mine![0].ClientId);
    }

    [Fact]
    public async Task Client_cannot_view_another_clients_order()
    {
        var created = await (await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 1)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();

        var response = await As(ClientB).GetAsync($"/api/supplies/orders/{created!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_view_and_list_any_order()
    {
        var created = await (await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 1)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();

        Assert.Equal(HttpStatusCode.OK, (await As(Admin).GetAsync($"/api/supplies/orders/{created!.Id}")).StatusCode);
        var all = await As(Admin).GetFromJsonAsync<List<SupplyOrderResponse>>("/api/supplies/orders");
        Assert.Contains(all!, o => o.Id == created.Id);
    }

    [Fact]
    public async Task Client_cannot_use_admin_list()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await As(ClientA).GetAsync("/api/supplies/orders")).StatusCode);
    }

    // ── Confirmar ──

    [Fact]
    public async Task Admin_confirms_pending_order()
    {
        var created = await (await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 1)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();

        var response = await As(Admin).PatchAsync($"/api/supplies/orders/{created!.Id}/confirm", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var confirmed = await response.Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Equal("confirmed", confirmed!.Status);
        Assert.Equal(2, confirmed.StatusHistory.Count);
        // Confirmar no toca el stock: sigue apartado desde que se creó el pedido.
        Assert.Equal(4, await _api.QueryAsync(db => db.SupplyProducts.Where(p => p.Id == KitId).Select(p => p.Stock).SingleAsync()));
    }

    [Fact]
    public async Task Cannot_confirm_an_already_confirmed_order()
    {
        var created = await (await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 1)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();
        await As(Admin).PatchAsync($"/api/supplies/orders/{created!.Id}/confirm", null);

        var response = await As(Admin).PatchAsync($"/api/supplies/orders/{created.Id}/confirm", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_unknown_order_is_404()
    {
        var response = await As(Admin).PatchAsync("/api/supplies/orders/999/confirm", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Client_cannot_confirm_orders()
    {
        var created = await (await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 1)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();

        var response = await As(ClientA).PatchAsync($"/api/supplies/orders/{created!.Id}/confirm", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Rechazar ──

    [Fact]
    public async Task Admin_rejects_order_restocks_inventory_and_records_reason()
    {
        var created = await (await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 2)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();

        var response = await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{created!.Id}/reject", new
        {
            reason = "El comprobante no corresponde a esta cuenta",
            refundProofUrl = "/uploads/attachments/devolucion.jpg",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rejected = await response.Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Equal("rejected", rejected!.Status);
        Assert.Equal("El comprobante no corresponde a esta cuenta", rejected.RejectionReason);
        Assert.Equal("/uploads/attachments/devolucion.jpg", rejected.RefundProofUrl);

        // El stock apartado (5 - 2 = 3) vuelve al inventario.
        Assert.Equal(5, await _api.QueryAsync(db => db.SupplyProducts.Where(p => p.Id == KitId).Select(p => p.Stock).SingleAsync()));
    }

    [Fact]
    public async Task Reject_requires_a_reason()
    {
        var created = await (await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 1)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();

        var response = await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{created!.Id}/reject", new { reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cannot_reject_an_already_confirmed_order()
    {
        var created = await (await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 1)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();
        await As(Admin).PatchAsync($"/api/supplies/orders/{created!.Id}/confirm", null);

        var response = await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{created.Id}/reject", new { reason = "Ya no aplica" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Client_cannot_reject_orders()
    {
        var created = await (await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 1)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();

        var response = await As(ClientA).PatchAsJsonAsync($"/api/supplies/orders/{created!.Id}/reject", new { reason = "No aplica" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Ciclo de vida: enviar / pago incompleto / completar ──

    private static object ShipPayload(string carrier = "Servientrega", string trackingNumber = "123456789", string photoUrl = "/uploads/attachments/paquete.jpg")
        => new { carrier, trackingNumber, photoUrl };

    private async Task<SupplyOrderResponse> CreateOrderAs(AppUser user, int quantity = 1)
        => (await (await As(user).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, quantity)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>())!;

    private async Task<SupplyOrderResponse> ConfirmedOrderOf(AppUser user, int quantity = 1)
    {
        var order = await CreateOrderAs(user, quantity);
        await As(Admin).PatchAsync($"/api/supplies/orders/{order.Id}/confirm", null);
        return order;
    }

    private async Task<SupplyOrderResponse> ShippedOrderOf(AppUser user)
    {
        var order = await ConfirmedOrderOf(user);
        await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/ship", ShipPayload());
        return order;
    }

    [Fact]
    public async Task Admin_ships_confirmed_order_with_shipment_data_and_history()
    {
        var order = await ConfirmedOrderOf(ClientA, 2);

        var response = await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/ship", ShipPayload());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var shipped = await response.Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Equal("shipped", shipped!.Status);
        Assert.NotNull(shipped.Shipment);
        Assert.Equal("Servientrega", shipped.Shipment!.Carrier);
        Assert.Equal("123456789", shipped.Shipment.TrackingNumber);
        Assert.Equal("/uploads/attachments/paquete.jpg", shipped.Shipment.PhotoUrl);
        Assert.Equal(new[] { "pending", "confirmed", "shipped" }, shipped.StatusHistory.Select(h => h.Status));
        // Despachar no toca el stock: ya se descontó al crear el pedido.
        Assert.Equal(3, await _api.QueryAsync(db => db.SupplyProducts.Where(p => p.Id == KitId).Select(p => p.Stock).SingleAsync()));
    }

    [Fact]
    public async Task Cannot_ship_a_pending_order()
    {
        var order = await CreateOrderAs(ClientA);

        var response = await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/ship", ShipPayload());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("", "123", "/uploads/attachments/p.jpg")]
    [InlineData("Servientrega", "  ", "/uploads/attachments/p.jpg")]
    [InlineData("Servientrega", "123", "")]
    public async Task Ship_requires_carrier_tracking_and_photo(string carrier, string tracking, string photo)
    {
        var order = await ConfirmedOrderOf(ClientA);

        var response = await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/ship", ShipPayload(carrier, tracking, photo));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var status = await _api.QueryAsync(db => db.SupplyOrders.Where(o => o.Id == order.Id).Select(o => o.Status).SingleAsync());
        Assert.Equal("confirmed", status);
    }

    [Fact]
    public async Task Client_cannot_ship_orders()
    {
        var order = await ConfirmedOrderOf(ClientA);

        var response = await As(ClientA).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/ship", ShipPayload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ship_unknown_order_is_404()
    {
        var response = await As(Admin).PatchAsJsonAsync("/api/supplies/orders/999/ship", ShipPayload());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_flags_payment_shortfall_and_order_stays_pending()
    {
        var order = await CreateOrderAs(ClientA, 2); // total 370000

        var response = await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/payment-shortfall", new
        {
            amount = 70000m,
            message = "El comprobante que enviaste no cubre el total del pedido.",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var flagged = await response.Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Equal("pending", flagged!.Status);
        Assert.Single(flagged.StatusHistory); // no es un cambio de estado
        Assert.NotNull(flagged.PaymentNote);
        Assert.Equal(70000m, flagged.PaymentNote!.Amount);
        Assert.Equal("El comprobante que enviaste no cubre el total del pedido.", flagged.PaymentNote.Message);

        // El cliente lo ve en "Mis pedidos".
        var mine = await As(ClientA).GetFromJsonAsync<List<SupplyOrderResponse>>("/api/supplies/orders/mine");
        Assert.Equal(70000m, mine!.Single().PaymentNote!.Amount);
    }

    [Fact]
    public async Task New_payment_shortfall_replaces_the_previous_one()
    {
        var order = await CreateOrderAs(ClientA, 2);
        await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/payment-shortfall", new { amount = 70000m, message = "Primero" });

        var response = await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/payment-shortfall", new { amount = 20000m, message = "Segundo" });

        var flagged = await response.Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Equal(20000m, flagged!.PaymentNote!.Amount);
        Assert.Equal("Segundo", flagged.PaymentNote.Message);
    }

    [Theory]
    [InlineData(0, "Falta")]
    [InlineData(-5, "Falta")]
    [InlineData(1000, "")]
    [InlineData(999999, "Más que el total")]
    public async Task Payment_shortfall_validates_amount_and_message(int amount, string message)
    {
        var order = await CreateOrderAs(ClientA); // total 185000

        var response = await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/payment-shortfall", new { amount = (decimal)amount, message });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cannot_flag_payment_shortfall_on_a_confirmed_order()
    {
        var order = await ConfirmedOrderOf(ClientA);

        var response = await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/payment-shortfall", new { amount = 1000m, message = "Falta" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Client_cannot_flag_payment_shortfall()
    {
        var order = await CreateOrderAs(ClientA);

        var response = await As(ClientA).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/payment-shortfall", new { amount = 1000m, message = "Falta" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Client_completes_their_shipped_order_with_full_history()
    {
        var order = await ShippedOrderOf(ClientA);

        var response = await As(ClientA).PatchAsync($"/api/supplies/orders/{order.Id}/complete", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var completed = await response.Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Equal("completed", completed!.Status);
        Assert.Equal(new[] { "pending", "confirmed", "shipped", "completed" }, completed.StatusHistory.Select(h => h.Status));
        Assert.NotNull(completed.Shipment); // los datos del envío se conservan
        Assert.Equal(completed.StatusHistory[^1].ChangedAt, completed.StatusUpdatedAt);
    }

    [Fact]
    public async Task Client_cannot_complete_another_clients_order()
    {
        var order = await ShippedOrderOf(ClientA);

        var response = await As(ClientB).PatchAsync($"/api/supplies/orders/{order.Id}/complete", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cannot_complete_an_order_that_was_not_shipped()
    {
        var order = await ConfirmedOrderOf(ClientA);

        var response = await As(ClientA).PatchAsync($"/api/supplies/orders/{order.Id}/complete", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_complete_a_walkin_order_without_account()
    {
        var created = await (await As(Admin).PostAsJsonAsync("/api/supplies/orders", OrderPayload(KitId, 1, clientId: null)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();
        await As(Admin).PatchAsync($"/api/supplies/orders/{created!.Id}/confirm", null);
        await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{created.Id}/ship", ShipPayload());

        var response = await As(Admin).PatchAsync($"/api/supplies/orders/{created.Id}/complete", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("completed", (await response.Content.ReadFromJsonAsync<SupplyOrderResponse>())!.Status);
    }

    [Fact]
    public async Task Rejected_order_cannot_be_shipped_or_completed()
    {
        var order = await CreateOrderAs(ClientA);
        await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/reject", new { reason = "Comprobante inválido" });

        Assert.Equal(HttpStatusCode.BadRequest, (await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/ship", ShipPayload())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await As(ClientA).PatchAsync($"/api/supplies/orders/{order.Id}/complete", null)).StatusCode);
    }

    // ── Avisos por correo (ver hallazgo 2.1: las 6 categorías de Suministros) ──

    [Fact]
    public async Task Confirming_an_order_notifies_the_customer()
    {
        var order = await CreateOrderAs(ClientA);

        await As(Admin).PatchAsync($"/api/supplies/orders/{order.Id}/confirm", null);

        var sent = Assert.Single(_api.SentNotifications, n => n.Category == "supplies.order-confirmed");
        Assert.Equal(ClientA.Email, sent.RecipientEmail);
        Assert.Contains($"#{order.Id}", sent.Body);
    }

    [Fact]
    public async Task Rejecting_an_order_notifies_the_customer_with_the_reason()
    {
        var order = await CreateOrderAs(ClientA);

        await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/reject", new { reason = "Comprobante inválido" });

        var sent = Assert.Single(_api.SentNotifications, n => n.Category == "supplies.order-rejected");
        Assert.Equal(ClientA.Email, sent.RecipientEmail);
        Assert.Contains("Comprobante inválido", sent.Body);
    }

    [Fact]
    public async Task Shipping_an_order_notifies_the_customer_with_a_delivery_confirmation_link()
    {
        var order = await ConfirmedOrderOf(ClientA);

        await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/ship", ShipPayload());

        var sent = Assert.Single(_api.SentNotifications, n => n.Category == "supplies.order-shipped");
        Assert.Equal(ClientA.Email, sent.RecipientEmail);
        Assert.Contains("/api/supplies/orders/confirm-delivery/", sent.Body);
    }

    [Fact]
    public async Task Payment_shortfall_notifies_the_customer_with_the_amount_and_message()
    {
        var order = await CreateOrderAs(ClientA, 2); // total 370000

        await As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{order.Id}/payment-shortfall", new { amount = 70000m, message = "Falta completar la transferencia" });

        var sent = Assert.Single(_api.SentNotifications, n => n.Category == "supplies.payment-shortfall");
        Assert.Equal(ClientA.Email, sent.RecipientEmail);
        Assert.Contains("70.000", sent.Body);
        Assert.Contains("Falta completar la transferencia", sent.Body);
    }

    [Fact]
    public async Task Registering_a_cliente_account_sends_a_welcome_notification()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/auth/register", new
        {
            name = "Nuevo Cliente",
            email = "nuevo.cliente@lidessa.test",
            password = "cliente123",
            role = "cliente",
            phone = "300 000 0000",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sent = Assert.Single(_api.SentNotifications, n => n.Category == "supplies.account-created");
        Assert.Equal("nuevo.cliente@lidessa.test", sent.RecipientEmail);
    }

    // ── Confirmación pública de entrega (botón del correo, sin login) ──

    private async Task<string> DeliveryTokenFor(long orderId) =>
        await _api.QueryAsync(db => db.SupplyOrderDeliveryTokens
            .Where(t => t.OrderId == orderId)
            .OrderByDescending(t => t.Id)
            .Select(t => t.Token)
            .FirstAsync());

    [Fact]
    public async Task Anonymous_confirms_delivery_with_a_valid_token()
    {
        var order = await ShippedOrderOf(ClientA);
        var token = await DeliveryTokenFor(order.Id);

        var response = await Anonymous().GetAsync($"/api/supplies/orders/confirm-delivery/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("completed", await _api.QueryAsync(db => db.SupplyOrders.Where(o => o.Id == order.Id).Select(o => o.Status).SingleAsync()));
        Assert.True(await _api.QueryAsync(db => db.SupplyOrderDeliveryTokens.Where(t => t.Token == token).Select(t => t.UsedAt != null).SingleAsync()));
        // La administradora queda notificada de que el cliente confirmó.
        Assert.Contains(_api.SentNotifications, n => n.Category == "supplies.delivery-confirmed" && n.RecipientEmail == Admin.Email);
    }

    [Fact]
    public async Task A_used_token_cannot_confirm_delivery_twice()
    {
        var order = await ShippedOrderOf(ClientA);
        var token = await DeliveryTokenFor(order.Id);
        await Anonymous().GetAsync($"/api/supplies/orders/confirm-delivery/{token}");

        var response = await Anonymous().GetAsync($"/api/supplies/orders/confirm-delivery/{token}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_token_is_rejected()
    {
        var response = await Anonymous().GetAsync("/api/supplies/orders/confirm-delivery/no-existe-este-token");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stale_link_is_rejected_if_the_order_was_already_completed_another_way()
    {
        // La administradora (o el cliente desde su cuenta) completó el
        // pedido antes de que se usara el enlace del correo — el token
        // sigue sin usar, pero el pedido ya no está "enviado".
        var order = await ShippedOrderOf(ClientA);
        var token = await DeliveryTokenFor(order.Id);
        await As(Admin).PatchAsync($"/api/supplies/orders/{order.Id}/complete", null);

        var response = await Anonymous().GetAsync($"/api/supplies/orders/confirm-delivery/{token}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
