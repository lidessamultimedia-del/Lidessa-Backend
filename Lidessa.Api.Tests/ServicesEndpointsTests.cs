using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lidessa.Api.Dtos.Services;
using Lidessa.Api.Models;
using Lidessa.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Tests;

// Escenario: dos categorías (una inactiva) y tres servicios — uno protegido
// (de los 16 originales), uno normal y uno inactivo.
public class ServicesEndpointsTests : IAsyncLifetime
{
    private readonly LmsApiFactory _api = new();

    private static readonly AppUser Admin = new() { Id = 1, Name = "admin", Email = "admin@lidessa.test", Role = "admin" };
    private static readonly AppUser Student = new() { Id = 10, Name = "estudiante", Email = "e@lidessa.test", Role = "estudiante" };

    private const long CategoryId = 1;
    private const long InactiveCategoryId = 2;
    private const long LockedServiceId = 10;
    private const long ServiceId = 11;
    private const long InactiveServiceId = 12;

    public async Task InitializeAsync()
    {
        await _api.SeedAsync(db =>
        {
            db.Users.AddRange(Admin, Student);
            db.ServiceCategories.AddRange(
                new ServiceCategory { Id = CategoryId, Name = "Gestión empresarial", Active = true },
                new ServiceCategory { Id = InactiveCategoryId, Name = "Propiedades horizontales", Active = false });
            db.Services.AddRange(
                Service(LockedServiceId, "gestion-de-calidad", "Gestión de Calidad", locked: true),
                Service(ServiceId, "consultoria", "Consultoría"),
                Service(InactiveServiceId, "oculto", "Oculto", active: false));
            return Task.CompletedTask;
        });
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private HttpClient Anonymous() => _api.CreateClient();

    private HttpClient As(AppUser user) => _api.ClientFor(user);

    private static Service Service(long id, string slug, string title, bool active = true, bool locked = false) => new()
    {
        Id = id,
        Slug = slug,
        CategoryId = CategoryId,
        Title = title,
        Description = "Desc",
        HeroImageUrl = "/assets/Ips.jpg",
        Active = active,
        Locked = locked,
        TabsJson = "[]",
    };

    private static object ServicePayload(string title = "Gestión Ambiental", long categoryId = CategoryId, bool active = true) => new
    {
        categoryId,
        title,
        description = "Descripción",
        heroImageUrl = "/uploads/attachments/foto.jpg",
        active,
        locked = false,
        tabs = new[] { new { label = "Información general", sections = new[] { new { heading = "Alcance", text = "Todo" } } } },
    };

    // ── Categorías ──

    [Fact]
    public async Task Anonymous_can_list_categories_including_inactive()
    {
        var response = await Anonymous().GetAsync("/api/service-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categories = await response.Content.ReadFromJsonAsync<List<ServiceCategoryResponse>>();
        Assert.Equal(new[] { "Gestión empresarial", "Propiedades horizontales" }, categories!.Select(c => c.Name));
        Assert.False(categories.Single(c => c.Id == InactiveCategoryId).Active);
    }

    [Fact]
    public async Task Anonymous_cannot_create_category()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/service-categories", new { name = "Nueva", active = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Student_cannot_create_category()
    {
        var response = await As(Student).PostAsJsonAsync("/api/service-categories", new { name = "Nueva", active = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_creates_category()
    {
        var response = await As(Admin).PostAsJsonAsync("/api/service-categories", new { name = "Asesoría jurídica", active = true });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ServiceCategoryResponse>();
        Assert.Equal("Asesoría jurídica", created!.Name);
        Assert.True(created.Active);
    }

    [Fact]
    public async Task Duplicate_category_name_is_rejected()
    {
        var response = await As(Admin).PostAsJsonAsync("/api/service-categories", new { name = "Gestión empresarial", active = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_renames_and_deactivates_category()
    {
        var response = await As(Admin).PutAsJsonAsync($"/api/service-categories/{CategoryId}", new { name = "Gestión", active = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var service = await Anonymous().GetFromJsonAsync<ServiceResponse>("/api/services/consultoria");
        Assert.Equal("Gestión", service!.CategoryName);
    }

    [Fact]
    public async Task Category_with_services_cannot_be_deleted()
    {
        var response = await As(Admin).DeleteAsync($"/api/service-categories/{CategoryId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Empty_category_is_deleted()
    {
        var response = await As(Admin).DeleteAsync($"/api/service-categories/{InactiveCategoryId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await _api.QueryAsync(db => db.ServiceCategories.AnyAsync(c => c.Id == InactiveCategoryId)));
    }

    // ── Servicios: lectura ──

    [Fact]
    public async Task Public_list_only_includes_active_services()
    {
        var services = await Anonymous().GetFromJsonAsync<List<ServiceResponse>>("/api/services");

        Assert.Equal(new[] { "consultoria", "gestion-de-calidad" }, services!.Select(s => s.Slug));
    }

    [Fact]
    public async Task Admin_list_includes_inactive_services()
    {
        var services = await As(Admin).GetFromJsonAsync<List<ServiceResponse>>("/api/services/admin");

        Assert.Equal(3, services!.Count);
        Assert.Contains(services, s => s.Slug == "oculto" && !s.Active);
    }

    [Fact]
    public async Task Anonymous_cannot_read_admin_list()
    {
        var response = await Anonymous().GetAsync("/api/services/admin");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Public_detail_by_slug_returns_category_and_tabs()
    {
        var service = await Anonymous().GetFromJsonAsync<ServiceResponse>("/api/services/gestion-de-calidad");

        Assert.Equal(LockedServiceId, service!.Id);
        Assert.Equal("Gestión empresarial", service.CategoryName);
        Assert.Equal("/assets/Ips.jpg", service.HeroImageUrl);
        Assert.True(service.Locked);
        Assert.Equal(JsonValueKind.Array, service.Tabs.ValueKind);
    }

    [Fact]
    public async Task Inactive_service_detail_is_hidden_from_public()
    {
        var response = await Anonymous().GetAsync("/api/services/oculto");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Servicios: escritura ──

    [Fact]
    public async Task Admin_creates_service_with_slug_without_accents_and_tabs()
    {
        var response = await As(Admin).PostAsJsonAsync("/api/services", ServicePayload());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ServiceResponse>();
        Assert.Equal("gestion-ambiental", created!.Slug);
        Assert.Equal("Gestión empresarial", created.CategoryName);
        Assert.Equal("/uploads/attachments/foto.jpg", created.HeroImageUrl);
        Assert.Equal("Alcance", created.Tabs[0].GetProperty("sections")[0].GetProperty("heading").GetString());
    }

    [Fact]
    public async Task Repeated_title_gets_numbered_slug()
    {
        var response = await As(Admin).PostAsJsonAsync("/api/services", ServicePayload(title: "Consultoría"));

        var created = await response.Content.ReadFromJsonAsync<ServiceResponse>();
        Assert.Equal("consultoria-2", created!.Slug);
    }

    [Fact]
    public async Task Create_with_unknown_category_is_rejected()
    {
        var response = await As(Admin).PostAsJsonAsync("/api/services", ServicePayload(categoryId: 999));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Student_cannot_create_service()
    {
        var response = await As(Student).PostAsJsonAsync("/api/services", ServicePayload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_deactivates_service_and_it_leaves_public_list()
    {
        var response = await As(Admin).PutAsJsonAsync($"/api/services/{ServiceId}", ServicePayload(title: "Consultoría", active: false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ServiceResponse>();
        Assert.Equal("consultoria", updated!.Slug); // Editar no cambia la URL.
        var publicList = await Anonymous().GetFromJsonAsync<List<ServiceResponse>>("/api/services");
        Assert.DoesNotContain(publicList!, s => s.Id == ServiceId);
    }

    [Fact]
    public async Task Update_missing_service_returns_404()
    {
        var response = await As(Admin).PutAsJsonAsync("/api/services/999", ServicePayload());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Locked_service_cannot_be_deleted()
    {
        var response = await As(Admin).DeleteAsync($"/api/services/{LockedServiceId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await _api.QueryAsync(db => db.Services.AnyAsync(s => s.Id == LockedServiceId)));
    }

    [Fact]
    public async Task Admin_deletes_unlocked_service()
    {
        var response = await As(Admin).DeleteAsync($"/api/services/{ServiceId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await _api.QueryAsync(db => db.Services.AnyAsync(s => s.Id == ServiceId)));
    }
}
