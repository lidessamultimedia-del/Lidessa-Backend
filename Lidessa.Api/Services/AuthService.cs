using Lidessa.Api.Data;
using Lidessa.Api.Dtos.Auth;
using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Services;

public class AuthService
{
    private static readonly string[] AllowedRoles = { "admin", "profesor", "estudiante" };

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

        return (new LoginResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            User = ToResponse(user),
        }, null);
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
