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
}
