using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lidessa.Api.Data;
using Lidessa.Api.Models;
using Lidessa.Api.Services;
using Lidessa.Api.Services.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lidessa.Api.Tests;

// Un error no controlado (acá, simulado con un INotificationService que
// lanza) no debe filtrar una página de error de ASP.NET ni una respuesta
// vacía: UseExceptionHandler en Program.cs tiene que devolver un 500 con el
// mismo shape `{ message }` que ya usan los 4xx de los servicios.
public class GlobalExceptionHandlerTests : IAsyncLifetime
{
    private readonly string _databaseName = $"lidessa-tests-{Guid.NewGuid()}";
    private WebApplicationFactory<Program> _factory = null!;

    static GlobalExceptionHandlerTests()
    {
        Environment.SetEnvironmentVariable("Jwt__Key", "clave-de-pruebas-larga-y-secreta-para-hmac-sha256");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", "Server=unused;Database=unused");
    }

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                var sqlServerOptions = services.Single(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                services.Remove(sqlServerOptions);
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));

                var notifications = services.Single(d => d.ServiceType == typeof(INotificationService));
                services.Remove(notifications);
                services.AddScoped<INotificationService, ThrowingNotificationService>();
            });
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new AppUser { Id = 1, Name = "Admin", Email = "admin@lidessa.test", Role = "admin", PasswordHash = "x" });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ExcepcionNoControlada_Devuelve500ConMensajeConsistente()
    {
        using var scope = _factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Id == 1);
        var (token, _) = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateToken(admin);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // InviteAsync guarda al usuario y después llama a INotificationService
        // (que acá lanza) para avisarle que active su cuenta.
        var response = await client.PostAsJsonAsync("/api/auth/invite", new
        {
            name = "Nuevo Profesor",
            email = "nuevo@lidessa.test",
            role = "profesor",
            phone = "",
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body);
        Assert.True(body!.ContainsKey("message"));
        // No se filtra el stack trace ni el tipo de excepción.
        Assert.DoesNotContain("Exception", body["message"]);
    }

    // INotificationService documenta que nunca debe lanzar (los fallos de
    // entrega se reportan devolviendo false); se viola eso a propósito acá
    // para simular cualquier excepción inesperada y probar que
    // UseExceptionHandler la atrapa igual.
    private class ThrowingNotificationService : INotificationService
    {
        public Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Servicio de correo caído (simulado para la prueba)");
    }
}
