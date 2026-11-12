using System.Net;
using System.Net.Http.Json;
using Lidessa.Api.Dtos.Auth;
using Lidessa.Api.Dtos.Certifications;
using Lidessa.Api.Dtos.Enrollments;
using Lidessa.Api.Models;
using Lidessa.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Tests;

// Cuentas vinculadas (ver 2.2 del plan): una cuenta puede tener más de un
// rol. Escenario: un admin, un estudiante y un cliente (cada uno con su
// rol principal) y un curso para probar que las verificaciones de
// inscripción/certificación/profesor reconocen también los roles agregados.
public class AccountRolesEndpointsTests : IAsyncLifetime
{
    private readonly LmsApiFactory _api = new();

    private static readonly AppUser Admin = new() { Id = 1, Name = "Admin", Email = "admin@lidessa.test", Role = "admin" };
    private static readonly AppUser Student = new() { Id = 10, Name = "Estudiante Uno", Email = "estudiante@lidessa.test", Role = "estudiante" };
    private static readonly AppUser ClientUser = new() { Id = 20, Name = "Cliente Uno", Email = "cliente@lidessa.test", Role = "cliente" };

    private const long CourseId = 100;

    public async Task InitializeAsync()
    {
        await _api.SeedAsync(db =>
        {
            db.Users.AddRange(Admin, Student, ClientUser);
            db.Courses.Add(new Course { Id = CourseId, Name = "Curso de prueba", Capacity = 30 });
            return Task.CompletedTask;
        });
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private HttpClient Anonymous() => _api.CreateClient();

    private HttpClient As(AppUser user) => _api.ClientFor(user);

    // GenerateToken necesita AppUser.Roles cargado (ver TokenService) — el
    // objeto que se sembró en el seed no lo refleja después de un cambio
    // hecho por la API, así que para simular "la persona vuelve a entrar
    // después de que le agregaron el rol" hay que releer la cuenta de la
    // base y emitir el token desde ahí, no desde el objeto en memoria.
    private async Task<HttpClient> AsFreshLogin(AppUser user)
    {
        var reloaded = await _api.QueryAsync(db => db.Users.Include(u => u.Roles).SingleAsync(u => u.Id == user.Id));
        return _api.ClientFor(reloaded);
    }

    [Fact]
    public async Task Admin_adds_a_role_and_notifies_the_account_owner()
    {
        var response = await As(Admin).PostAsJsonAsync($"/api/auth/users/{Student.Id}/roles", new { role = "cliente" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("estudiante", updated!.Role); // el rol principal no cambia
        Assert.Equal(new[] { "estudiante", "cliente" }, updated.Roles);

        var sent = Assert.Single(_api.SentNotifications, n => n.Category == "account.role-added");
        Assert.Equal(Student.Email, sent.RecipientEmail);
        Assert.Contains("cliente", sent.Body);
    }

    [Fact]
    public async Task Cannot_add_the_role_the_account_already_has_as_primary()
    {
        var response = await As(Admin).PostAsJsonAsync($"/api/auth/users/{Student.Id}/roles", new { role = "estudiante" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cannot_add_a_role_already_linked()
    {
        await As(Admin).PostAsJsonAsync($"/api/auth/users/{Student.Id}/roles", new { role = "cliente" });

        var response = await As(Admin).PostAsJsonAsync($"/api/auth/users/{Student.Id}/roles", new { role = "cliente" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var roles = await _api.QueryAsync(db => db.UserRoles.Where(r => r.UserId == Student.Id).Select(r => r.Role).ToListAsync());
        Assert.Single(roles, "cliente"); // no quedó duplicado
    }

    [Theory]
    [InlineData("admin")] // no se agrega por este endpoint, aunque sea un rol válido del sistema
    [InlineData("gerente")]
    [InlineData("")]
    public async Task Rejects_roles_outside_profesor_estudiante_cliente(string role)
    {
        var response = await As(Admin).PostAsJsonAsync($"/api/auth/users/{Student.Id}/roles", new { role });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_404_for_unknown_account()
    {
        var response = await As(Admin).PostAsJsonAsync("/api/auth/users/9999/roles", new { role = "cliente" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Only_admin_can_add_roles()
    {
        Assert.Equal(HttpStatusCode.Forbidden,
            (await As(Student).PostAsJsonAsync($"/api/auth/users/{ClientUser.Id}/roles", new { role = "profesor" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Anonymous().PostAsJsonAsync($"/api/auth/users/{ClientUser.Id}/roles", new { role = "profesor" })).StatusCode);
    }

    [Fact]
    public async Task A_fresh_token_after_adding_a_role_grants_access_to_the_new_resource()
    {
        await As(Admin).PostAsJsonAsync($"/api/auth/users/{Student.Id}/roles", new { role = "cliente" });

        var client = await AsFreshLogin(Student);

        // Ya puede usar lo de Suministros (rol nuevo)...
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/supplies/orders/mine")).StatusCode);
        // ...sin perder lo del LMS (rol original).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/courses/mine")).StatusCode);
    }

    [Fact]
    public async Task Linked_role_is_recognized_for_course_enrollment()
    {
        await As(Admin).PostAsJsonAsync($"/api/auth/users/{ClientUser.Id}/roles", new { role = "estudiante" });

        var response = await As(Admin).PostAsJsonAsync($"/api/courses/{CourseId}/enrollments", new EnrollmentRequest { StudentId = ClientUser.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Linked_role_is_recognized_for_certification()
    {
        await As(Admin).PostAsJsonAsync($"/api/auth/users/{ClientUser.Id}/roles", new { role = "estudiante" });
        await _api.SeedAsync(db =>
        {
            db.CourseEnrollments.Add(new CourseEnrollment { CourseId = CourseId, StudentId = ClientUser.Id });
            return Task.CompletedTask;
        });

        var response = await As(Admin).PostAsJsonAsync("/api/certifications", new CertificationRequest { StudentId = ClientUser.Id, CourseId = CourseId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Linked_role_is_recognized_for_teacher_assignment()
    {
        await As(Admin).PostAsJsonAsync($"/api/auth/users/{ClientUser.Id}/roles", new { role = "profesor" });

        var response = await As(Admin).PutAsJsonAsync($"/api/courses/{CourseId}", new
        {
            name = "Curso de prueba",
            format = "topics",
            capacity = 30,
            teacherId = ClientUser.Id,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task User_list_role_filter_includes_linked_accounts()
    {
        await As(Admin).PostAsJsonAsync($"/api/auth/users/{Student.Id}/roles", new { role = "cliente" });

        var clients = await As(Admin).GetFromJsonAsync<List<UserResponse>>("/api/auth/users?role=cliente");

        Assert.Contains(clients!, u => u.Id == ClientUser.Id); // rol principal
        Assert.Contains(clients!, u => u.Id == Student.Id);    // rol vinculado
    }
}
