using System.Net;
using System.Net.Http.Json;
using Lidessa.Api.Dtos.Auth;
using Lidessa.Api.Dtos.Supplies;
using Lidessa.Api.Models;
using Lidessa.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Tests;

// Escenario: dos productos activos y uno oculto, un admin y una cuenta de cliente.
public class SupplyProductsEndpointsTests : IAsyncLifetime
{
    private readonly LmsApiFactory _api = new();

    private static readonly AppUser Admin = new() { Id = 1, Name = "admin", Email = "admin@lidessa.test", Role = "admin" };
    private static readonly AppUser Client = new() { Id = 20, Name = "cliente", Email = "cliente@lidessa.test", Role = "cliente" };

    private const long KitId = 1;
    private const long HelmetId = 2;
    private const long HiddenId = 3;

    public async Task InitializeAsync()
    {
        await _api.SeedAsync(db =>
        {
            db.Users.AddRange(Admin, Client);
            db.SupplyProducts.AddRange(
                Product(KitId, "Botiquín tipo A", 185000, daysAgo: 3),
                Product(HelmetId, "Casco", 65000, daysAgo: 1),
                Product(HiddenId, "Oculto", 1000, daysAgo: 2, active: false));
            return Task.CompletedTask;
        });
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private HttpClient Anonymous() => _api.CreateClient();

    private HttpClient As(AppUser user) => _api.ClientFor(user);

    private static SupplyProduct Product(long id, string name, decimal price, int daysAgo, bool active = true) => new()
    {
        Id = id,
        Name = name,
        Description = "Desc",
        Price = price,
        ImageUrl = "/assets/insumos.png",
        Stock = 10,
        Active = active,
        CreatedAt = DateTime.UtcNow.AddDays(-daysAgo),
        UpdatedAt = DateTime.UtcNow.AddDays(-daysAgo),
    };

    private static object ProductPayload(string name = "Gafas de protección", decimal price = 22000, int stock = 50, bool active = true) => new
    {
        name,
        description = "Certificadas ANSI Z87.1",
        price,
        stock,
        imageUrl = "/uploads/attachments/gafas.jpg",
        active,
    };

    // ── Lectura ──

    [Fact]
    public async Task Anonymous_lists_only_active_products_newest_first()
    {
        var products = await Anonymous().GetFromJsonAsync<List<SupplyProductResponse>>("/api/supplies/products");

        Assert.Equal(new[] { HelmetId, KitId }, products!.Select(p => p.Id));
    }

    [Fact]
    public async Task Admin_list_includes_hidden_products()
    {
        var products = await As(Admin).GetFromJsonAsync<List<SupplyProductResponse>>("/api/supplies/products/admin");

        Assert.Equal(new[] { HelmetId, HiddenId, KitId }, products!.Select(p => p.Id));
    }

    [Fact]
    public async Task Client_cannot_use_admin_list()
    {
        var response = await As(Client).GetAsync("/api/supplies/products/admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Hidden_product_detail_is_404_for_public_but_visible_to_admin()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/api/supplies/products/{HiddenId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Client).GetAsync($"/api/supplies/products/{HiddenId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await As(Admin).GetAsync($"/api/supplies/products/{HiddenId}")).StatusCode);
    }

    // ── Escritura ──

    [Fact]
    public async Task Admin_creates_product()
    {
        var response = await As(Admin).PostAsJsonAsync("/api/supplies/products", ProductPayload());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<SupplyProductResponse>();
        Assert.Equal("Gafas de protección", created!.Name);
        Assert.Equal(22000m, created.Price);
        Assert.Equal(50, created.Stock);
        Assert.True(created.Active);

        var stored = await _api.QueryAsync(db => db.SupplyProducts.SingleAsync(p => p.Id == created.Id));
        Assert.Equal("/uploads/attachments/gafas.jpg", stored.ImageUrl);
    }

    [Fact]
    public async Task Non_admin_cannot_write()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous().PostAsJsonAsync("/api/supplies/products", ProductPayload())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(Client).PostAsJsonAsync("/api/supplies/products", ProductPayload())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(Client).PutAsJsonAsync($"/api/supplies/products/{KitId}", ProductPayload())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(Client).PatchAsync($"/api/supplies/products/{KitId}/toggle-active", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(Client).DeleteAsync($"/api/supplies/products/{KitId}")).StatusCode);
    }

    [Theory]
    [InlineData("", 1000, 0)]       // sin nombre
    [InlineData("Casco", 0, 0)]     // precio 0
    [InlineData("Casco", 1000, -1)] // stock negativo
    public async Task Invalid_product_is_rejected(string name, int price, int stock)
    {
        var response = await As(Admin).PostAsJsonAsync("/api/supplies/products", ProductPayload(name, price, stock));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_updates_product()
    {
        var response = await As(Admin).PutAsJsonAsync($"/api/supplies/products/{KitId}", ProductPayload("Botiquín tipo B", 99000, 3, active: false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await _api.QueryAsync(db => db.SupplyProducts.SingleAsync(p => p.Id == KitId));
        Assert.Equal("Botiquín tipo B", stored.Name);
        Assert.Equal(99000m, stored.Price);
        Assert.Equal(3, stored.Stock);
        Assert.False(stored.Active);
    }

    [Fact]
    public async Task Update_unknown_product_is_404()
    {
        var response = await As(Admin).PutAsJsonAsync("/api/supplies/products/999", ProductPayload());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Toggle_active_flips_visibility()
    {
        var hidden = await (await As(Admin).PatchAsync($"/api/supplies/products/{KitId}/toggle-active", null))
            .Content.ReadFromJsonAsync<SupplyProductResponse>();
        Assert.False(hidden!.Active);

        var shown = await (await As(Admin).PatchAsync($"/api/supplies/products/{KitId}/toggle-active", null))
            .Content.ReadFromJsonAsync<SupplyProductResponse>();
        Assert.True(shown!.Active);
    }

    [Fact]
    public async Task Admin_deletes_product()
    {
        var response = await As(Admin).DeleteAsync($"/api/supplies/products/{HelmetId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await _api.QueryAsync(db => db.SupplyProducts.AnyAsync(p => p.Id == HelmetId)));
        Assert.Equal(HttpStatusCode.NotFound, (await As(Admin).DeleteAsync($"/api/supplies/products/{HelmetId}")).StatusCode);
    }

    // ── Rol cliente ──

    [Fact]
    public async Task Public_register_accepts_cliente_role()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/auth/register", new
        {
            name = "Constructora Andina",
            email = "compras@andina.test",
            password = "cliente123",
            role = "cliente",
            phone = "+57 310 555 2048",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("cliente", user!.Role);
    }

    [Fact]
    public async Task Client_cannot_enroll_in_courses()
    {
        await _api.SeedAsync(db =>
        {
            db.Courses.Add(new Course { Id = 100, Name = "Liderazgo" });
            return Task.CompletedTask;
        });

        var response = await As(Admin).PostAsJsonAsync("/api/courses/100/enrollments", new { studentId = Client.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
