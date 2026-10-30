using System.Text;
using Lidessa.Api.Data;
using Lidessa.Api.Services;
using Lidessa.Api.Services.Notifications;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<DocumentTypeService>();
builder.Services.AddScoped<PersonProfileService>();
builder.Services.AddScoped<CourseService>();
builder.Services.AddScoped<AvatarService>();
builder.Services.AddScoped<AttachmentService>();
builder.Services.AddScoped<TopicService>();
builder.Services.AddScoped<LessonService>();
builder.Services.AddScoped<CourseEnrollmentService>();
builder.Services.AddScoped<AssignmentService>();
builder.Services.AddScoped<SubmissionService>();
builder.Services.AddScoped<QuizService>();
builder.Services.AddScoped<QuizAttemptService>();
builder.Services.AddScoped<LessonProgressService>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddScoped<CertificationService>();
builder.Services.AddScoped<BlogPostService>();
builder.Services.AddScoped<PqrsfTicketService>();
builder.Services.AddScoped<ServiceCategoryService>();
builder.Services.AddScoped<ServicesService>();
builder.Services.AddScoped<SiteSettingsService>();

// Avisos a usuarios (PQRSF y los que vengan). Por ahora se escriben en la
// consola; para enviar correos de verdad, registrar aquí otra implementación
// de INotificationService.
builder.Services.AddScoped<INotificationService, ConsoleNotificationService>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<FrontendOptions>(builder.Configuration.GetSection(FrontendOptions.SectionName));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Falta la sección 'Jwt' en la configuración.");
if (string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    throw new InvalidOperationException(
        "Falta configurar Jwt:Key. Corre: dotnet user-secrets set \"Jwt:Key\" \"<clave-larga-y-secreta>\"");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization();

const string FrontendCorsPolicy = "FrontendCorsPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins("https://localhost:8443", "http://localhost:8443")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Red de seguridad para cualquier excepción no controlada (bug real, caída de
// la base, etc.): sin esto, cada controlador tendría que armar su propio
// try/catch y la respuesta quedaría inconsistente (o vacía) frente a los 4xx
// ya uniformes (`{ message }`) que ya devuelven los servicios. En desarrollo
// se expone el mensaje real de la excepción para depurar más rápido; en
// cualquier otro ambiente, el mensaje genérico (no se filtran detalles internos).
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        if (feature is not null)
        {
            context.RequestServices.GetRequiredService<ILogger<Program>>()
                .LogError(feature.Error, "Error no controlado en {Path}", feature.Path);
        }

        var message = app.Environment.IsDevelopment() && feature is not null
            ? feature.Error.Message
            : "Ocurrió un error inesperado.";

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { message });
    });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = string.Empty;
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Lidessa.Api v1");
    });
}
else
{
    // En desarrollo no forzamos HTTPS (evita el warning cuando se corre solo
    // con el perfil "http"); en producción sí se exige.
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Expuesto para las pruebas de integración (WebApplicationFactory<Program>).
public partial class Program { }
