using System.Security.Cryptography;
using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Auth;
using Lidessa.Api.Models;
using Lidessa.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lidessa.Api.Services;

public class AuthService
{
    private static readonly string[] AllowedRoles = { "admin", "profesor", "estudiante" };
    private static readonly string[] InvitableRoles = { "profesor", "estudiante" };
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan ResetCodeLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan InviteTokenLifetime = TimeSpan.FromDays(7);

    private readonly AppDbContext _db;
    private readonly TokenService _tokenService;
    private readonly INotificationService _notifications;
    private readonly FrontendOptions _frontendOptions;

    public AuthService(AppDbContext db, TokenService tokenService, INotificationService notifications, IOptions<FrontendOptions> frontendOptions)
    {
        _db = db;
        _tokenService = tokenService;
        _notifications = notifications;
        _frontendOptions = frontendOptions.Value;
    }

    public async Task<(UserResponse? User, string? Error)> RegisterAsync(RegisterRequest request)
    {
        if (!AllowedRoles.Contains(request.Role))
        {
            return (null, $"Role debe ser uno de: {string.Join(", ", AllowedRoles)}");
        }

        var emailTaken = await _db.Users.AnyAsync(u => u.Email == request.Email);
        if (emailTaken)
        {
            return (null, "Ya existe una cuenta con ese correo");
        }

        var user = new AppUser
        {
            Name = request.Name,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = request.Role,
            Phone = request.Phone,
            Active = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return (ToResponse(user), null);
    }

    // El admin crea la cuenta de profesor/estudiante sin que la persona ponga
    // su propia contraseña — queda inactiva hasta que la active con el token
    // que recibe por el mismo INotificationService que usa el resto del sistema.
    public async Task<(UserResponse? User, string? Error)> InviteAsync(InviteAccountRequest request)
    {
        if (!InvitableRoles.Contains(request.Role))
        {
            return (null, $"Role debe ser uno de: {string.Join(", ", InvitableRoles)}");
        }

        var emailTaken = await _db.Users.AnyAsync(u => u.Email == request.Email);
        if (emailTaken)
        {
            return (null, "Ya existe una cuenta con ese correo");
        }

        var user = new AppUser
        {
            Name = request.Name,
            Email = request.Email,
            // Hash aleatorio e inútil — nadie puede loguearse con esto; la
            // cuenta queda Active=false hasta que se active con su propio token.
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            Role = request.Role,
            Phone = request.Phone,
            Active = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var token = GenerateUrlSafeToken();
        _db.AccountInviteTokens.Add(new AccountInviteToken
        {
            UserId = user.Id,
            Token = token,
            ExpiresAt = DateTime.UtcNow.Add(InviteTokenLifetime),
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var roleLabel = request.Role == "profesor" ? "profesor" : "estudiante";
        var activationLink = $"{_frontendOptions.BaseUrl}/activar-cuenta?token={token}";
        await _notifications.SendAsync(new NotificationMessage
        {
            RecipientEmail = user.Email,
            RecipientName = user.Name,
            RecipientUserId = user.Id,
            Subject = "Active su cuenta en Lidessa",
            Body = $"Le crearon una cuenta de {roleLabel} en Lidessa. Entre a este enlace para elegir su contraseña y activarla: {activationLink} (válido 7 días).",
            Category = NotificationCategories.AccountInvite,
        });

        return (ToResponse(user), null);
    }

    public async Task<string?> ActivateAccountAsync(ActivateAccountRequest request)
    {
        var inviteToken = await _db.AccountInviteTokens
            .Include(t => t.User)
            .Where(t => t.Token == request.Token && t.UsedAt == null)
            .FirstOrDefaultAsync();

        if (inviteToken is null || inviteToken.ExpiresAt <= DateTime.UtcNow)
        {
            return "El código de activación no es válido o ya expiró";
        }

        inviteToken.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        inviteToken.User.Active = true;
        inviteToken.User.UpdatedAt = DateTime.UtcNow;
        inviteToken.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return null;
    }

    public async Task<(LoginResponse? Result, string? Error)> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Email == request.Email);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return (null, "Correo o contraseña incorrectos");
        }

        if (!user.Active)
        {
            return (null, "La cuenta está desactivada");
        }

        var (token, expiresAt) = _tokenService.GenerateToken(user);
        var refreshToken = await CreateSessionAsync(user.Id);

        return (new LoginResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            RefreshToken = refreshToken,
            User = ToResponse(user),
        }, null);
    }

    public async Task<(RefreshResponse? Result, string? Error)> RefreshAsync(RefreshTokenRequest request)
    {
        var tokenHash = HashToken(request.RefreshToken);
        var session = await _db.UserSessions
            .Include(s => s.User)
            .SingleOrDefaultAsync(s => s.TokenHash == tokenHash);

        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= DateTime.UtcNow)
        {
            return (null, "El refresh token no es válido o ya expiró");
        }

        // Rotación: se revoca el token usado y se emite uno nuevo, para que un
        // token robado y ya usado no se pueda reutilizar en paralelo.
        session.RevokedAt = DateTime.UtcNow;
        var (accessToken, expiresAt) = _tokenService.GenerateToken(session.User);
        var newRefreshToken = await CreateSessionAsync(session.UserId); // guarda tambien la revocacion de arriba

        return (new RefreshResponse
        {
            Token = accessToken,
            ExpiresAt = expiresAt,
            RefreshToken = newRefreshToken,
        }, null);
    }

    public async Task LogoutAsync(RefreshTokenRequest request)
    {
        var tokenHash = HashToken(request.RefreshToken);
        var session = await _db.UserSessions.SingleOrDefaultAsync(s => s.TokenHash == tokenHash);
        if (session is not null && session.RevokedAt is null)
        {
            session.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<string?> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Email == request.Email);
        if (user is null)
        {
            // No se revela si el correo existe o no.
            return null;
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _db.PasswordResetCodes.Add(new PasswordResetCode
        {
            UserId = user.Id,
            Code = code,
            ExpiresAt = DateTime.UtcNow.Add(ResetCodeLifetime),
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        // TODO(backend): enviar el código por correo real en vez de devolverlo
        // aquí — mismo pendiente que PQRSFContext.jsx tenía en el frontend.
        return code;
    }

    public async Task<string?> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Email == request.Email);
        if (user is null)
        {
            return "Correo o código inválido";
        }

        var resetCode = await _db.PasswordResetCodes
            .Where(c => c.UserId == user.Id && c.Code == request.Code && c.UsedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        if (resetCode is null || resetCode.ExpiresAt <= DateTime.UtcNow)
        {
            return "Correo o código inválido";
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        // Si la cuenta todavía estaba pendiente de activar (invitación cuyo
        // código de 7 días venció), un código de recuperación válido demuestra
        // igual de bien que es dueño del correo — se activa aquí también, si no,
        // quedaría con contraseña nueva pero sin poder entrar nunca.
        user.Active = true;
        resetCode.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return null;
    }

    private async Task<string> CreateSessionAsync(long userId)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        _db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            TokenHash = HashToken(rawToken),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime),
        });
        await _db.SaveChangesAsync();
        return rawToken;
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    private static string GenerateUrlSafeToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static UserResponse ToResponse(AppUser user) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
        Role = user.Role,
        Phone = user.Phone,
        AvatarUrl = user.AvatarUrl,
    };
}
