using System.Net;
using System.Net.Http.Json;
using Lidessa.Api.Dtos.Supplies;
using Lidessa.Api.Models;
using Lidessa.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Tests;

// Dos cosas que el recorrido e2e contra SQL Server (e2e/roles-supplies.e2e.mjs)
// destapó en Suministros:
// 1. Concurrencia: 20 pedidos simultáneos por un stock de 3 se aceptaban
//    todos (lectura-comparación-escritura sin control). Ahora Stock y Status
//    son tokens de concurrencia y el servicio reintenta.
// 2. Cuentas vinculadas: lo que puede hacer una cuenta con más de un rol
//    en la tienda (estudiante+cliente, y admin+cliente).
//
// Ojo con InMemory: no es transaccional. Cuando un SaveChanges choca por
// concurrencia, las filas que iban antes en el mismo lote (el INSERT del
// pedido, su historial) quedan escritas igual — en SQL Server se deshacen
// con la transacción. Por eso aquí solo se verifica lo que protegen los
// tokens (respuestas HTTP, Stock y Status); que no queden pedidos huérfanos
// en la base real lo verifica el e2e (sección 9), que cuenta las líneas de
// pedido directamente en SQL Server.
public class SupplyOrdersConcurrencyAndLinkedRolesTests : IAsyncLifetime
{
    private readonly LmsApiFactory _api = new();

    private static readonly AppUser Admin = new() { Id = 1, Name = "admin", Email = "admin@lidessa.test", Role = "admin" };
    private static readonly AppUser ClientA = new() { Id = 20, Name = "Cliente A", Email = "a@lidessa.test", Role = "cliente" };
    private static readonly AppUser ClientB = new() { Id = 21, Name = "Cliente B", Email = "b@lidessa.test", Role = "cliente" };

    // Estudiante del CEET al que el admin le agregó el rol cliente.
    private static readonly AppUser StudentClient = new()
    {
        Id = 30, Name = "Estudiante y cliente", Email = "doble@lidessa.test", Role = "estudiante",
        Roles = { new AppUserRole { Role = "estudiante" }, new AppUserRole { Role = "cliente", GrantedByUserId = 1 } },
    };

    // Admin que además compra en la tienda con la misma cuenta.
    private static readonly AppUser AdminClient = new()
    {
        Id = 2, Name = "Admin y cliente", Email = "admin2@lidessa.test", Role = "admin",
        Roles = { new AppUserRole { Role = "admin" }, new AppUserRole { Role = "cliente", GrantedByUserId = 1 } },
    };

    private const long ScarceId = 1;
    private const long PlentyId = 2;
    private const int ScarceStock = 3;

    public async Task InitializeAsync()
    {
        await _api.SeedAsync(db =>
        {
            db.Users.AddRange(Admin, ClientA, ClientB, StudentClient, AdminClient);
            db.SupplyProducts.AddRange(
                new SupplyProduct { Id = ScarceId, Name = "Escaso", Description = "", Price = 1000, Stock = ScarceStock, Active = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SupplyProduct { Id = PlentyId, Name = "Abundante", Description = "", Price = 500, Stock = 1000, Active = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            return Task.CompletedTask;
        });
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private HttpClient As(AppUser user) => _api.ClientFor(user);

    private static object OrderPayload(long productId, int quantity = 1, long? clientId = null) => new
    {
        clientId,
        customerName = "Cliente",
        customerPhone = "3001234567",
        customerEmail = "cliente@lidessa.test",
        address = "Calle 1",
        items = new[] { new { productId, quantity } },
    };

    private Task<int> StockOf(long productId) =>
        _api.QueryAsync(db => db.SupplyProducts.Where(p => p.Id == productId).Select(p => p.Stock).SingleAsync());

    // ── Concurrencia ──

    [Fact]
    public async Task Simultaneous_orders_never_sell_more_than_the_stock()
    {
        var buyers = new[] { ClientA, ClientB, StudentClient };
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            As(buyers[i % buyers.Length]).PostAsJsonAsync("/api/supplies/orders", OrderPayload(ScarceId))));

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        Assert.Equal(ScarceStock, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        // Los que llegaron tarde: o vieron el stock agotado (400), o se les
        // acabaron los reintentos (409). Nunca un pedido de más.
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));

        Assert.Equal(0, await StockOf(ScarceId));
    }

    [Fact]
    public async Task Simultaneous_rejections_return_the_stock_only_once()
    {
        var created = await (await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(ScarceId, 2)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Equal(1, await StockOf(ScarceId));

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            As(Admin).PatchAsJsonAsync($"/api/supplies/orders/{created!.Id}/reject", new { reason = "Comprobante ilegible" })));

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(ScarceStock, await StockOf(ScarceId)); // 1 + 2, no 1 + 2*6
    }

    [Fact]
    public async Task Simultaneous_confirm_and_reject_leave_a_consistent_order()
    {
        var created = await (await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(ScarceId, 1)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();

        var admin = As(Admin);
        var results = await Task.WhenAll(
            admin.PatchAsync($"/api/supplies/orders/{created!.Id}/confirm", null),
            admin.PatchAsJsonAsync($"/api/supplies/orders/{created.Id}/reject", new { reason = "x" }));

        Assert.DoesNotContain(results, r => (int)r.StatusCode >= 500);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);

        var order = await _api.QueryAsync(db => db.SupplyOrders.SingleAsync(o => o.Id == created.Id));
        var winner = results.Single(r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(order.Status, (await winner.Content.ReadFromJsonAsync<SupplyOrderResponse>())!.Status);
        // Si ganó el rechazo, el stock volvió; si ganó la confirmación, sigue apartado.
        Assert.Equal(order.Status == "rejected" ? ScarceStock : ScarceStock - 1, await StockOf(ScarceId));
    }

    [Fact]
    public async Task Admin_product_edit_during_orders_does_not_fail()
    {
        var orders = Enumerable.Range(0, 10).Select(_ =>
            As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(PlentyId)));
        var toggles = Enumerable.Range(0, 4).Select(_ =>
            As(Admin).PatchAsync($"/api/supplies/products/{PlentyId}/toggle-active", null));

        var responses = await Task.WhenAll(orders.Concat(toggles));

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
    }

    // ── Cuentas vinculadas ──

    [Fact]
    public async Task Student_with_linked_client_role_buys_and_sees_only_own_orders()
    {
        var response = await As(StudentClient).PostAsJsonAsync("/api/supplies/orders", OrderPayload(PlentyId, 1, clientId: ClientA.Id));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Equal(StudentClient.Id, order!.ClientId); // no puede comprar a nombre de otro

        await As(ClientA).PostAsJsonAsync("/api/supplies/orders", OrderPayload(PlentyId));

        var mine = await As(StudentClient).GetFromJsonAsync<List<SupplyOrderResponse>>("/api/supplies/orders/mine");
        Assert.Equal(new[] { order.Id }, mine!.Select(o => o.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await As(StudentClient).GetAsync("/api/supplies/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(StudentClient).PatchAsync($"/api/supplies/orders/{order.Id}/confirm", null)).StatusCode);
    }

    [Fact]
    public async Task Student_with_linked_client_role_cannot_manage_the_catalog()
    {
        var response = await As(StudentClient).PostAsJsonAsync("/api/supplies/products", new { name = "x", price = 1, stock = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_with_linked_client_role_still_creates_walk_in_orders_for_other_accounts()
    {
        // Sin cuenta (mostrador).
        var walkIn = await (await As(AdminClient).PostAsJsonAsync("/api/supplies/orders", OrderPayload(PlentyId)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Null(walkIn!.ClientId);

        // Ligado a otro cliente.
        var forB = await (await As(AdminClient).PostAsJsonAsync("/api/supplies/orders", OrderPayload(PlentyId, clientId: ClientB.Id)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Equal(ClientB.Id, forB!.ClientId);

        // Y su propia compra (ClientStore manda clientId = su id) queda a su nombre.
        var own = await (await As(AdminClient).PostAsJsonAsync("/api/supplies/orders", OrderPayload(PlentyId, clientId: AdminClient.Id)))
            .Content.ReadFromJsonAsync<SupplyOrderResponse>();
        Assert.Equal(AdminClient.Id, own!.ClientId);
        var mine = await As(AdminClient).GetFromJsonAsync<List<SupplyOrderResponse>>("/api/supplies/orders/mine");
        Assert.Equal(new[] { own.Id }, mine!.Select(o => o.Id));
    }
}
