using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Lidessa.Api.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Lidessa.Api.Services;

public class TokenService
{
    private readonly JwtOptions _options;

    public TokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            // Rol principal primero: el código que lee un solo rol con
            // FindFirstValue(ClaimTypes.Role) sigue viendo el mismo de antes.
            new(ClaimTypes.Role, user.Role),
        };

        // Cuentas vinculadas (ver 2.2): un claim por cada rol extra, así
        // [Authorize(Roles = ...)] e IsInRole(...) reconocen todos los roles.
        // Requiere que user.Roles venga cargado (Include) — si no, la cuenta
        // solo lleva su rol principal.
        foreach (var role in user.Roles.Select(r => r.Role).Where(r => r != user.Role).Distinct())
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.ExpiresMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
