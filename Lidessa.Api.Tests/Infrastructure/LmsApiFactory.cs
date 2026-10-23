using System.Net.Http.Headers;
using Lidessa.Api.Data;
using Lidessa.Api.Models;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lidessa.Api.Tests.Infrastructure;

// Levanta la API real (controladores, auth JWT, servicios) contra una base
// EF InMemory propia — cada instancia es una base vacía, así las pruebas no
// se pisan entre sí.
public class LmsApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"lidessa-tests-{Guid.NewGuid()}";

    static LmsApiFactory()
    {
        // Program.cs lee Jwt:Key antes de Build(), así que tiene que estar
        // en la configuración desde el arranque (las variables de entorno sí).
        Environment.SetEnvironmentVariable("Jwt__Key", "clave-de-pruebas-larga-y-secreta-para-hmac-sha256");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", "Server=unused;Database=unused");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var sqlServerOptions = services.Single(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            services.Remove(sqlServerOptions);
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }

    public async Task SeedAsync(Func<AppDbContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await seed(db);
        await db.SaveChangesAsync();
    }

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // Token emitido por el TokenService real, con los mismos claims que el login.
    public HttpClient ClientFor(AppUser user)
    {
        using var scope = Services.CreateScope();
        var (token, _) = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateToken(user);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
