using System.Security.Cryptography;
using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Auth;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class AuthService
{
    private static readonly string[] AllowedRoles = { "admin", "profesor", "estudiante" };
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan ResetCodeLifetime = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _db;
    private readonly TokenService _tokenService;

    public AuthService(AppDbContext db, TokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
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
