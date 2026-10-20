using System.Security.Claims;
using Lidessa.Api.Dtos.Auth;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    // Registro público: sin sesión solo se pueden crear cuentas de estudiante.
    // Profesores y admins solo los puede crear un admin logueado (el token se
    // lee aunque el endpoint no exija [Authorize]). Si no fuera así, cualquiera
    // podría registrarse como admin.
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var isStaffRole = request.Role is "admin" or "profesor";
        if (isStaffRole && !User.IsInRole("admin"))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Solo un administrador puede crear cuentas de profesor o administrador" });
        }

        var (user, error) = await _authService.RegisterAsync(request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return CreatedAtAction(nameof(Register), new { id = user!.Id }, user);
    }

    // El admin crea cuentas de profesor/estudiante sin que la persona ponga
    // su propia contraseña — le llega un aviso para activarla.
    [Authorize(Roles = "admin")]
    [HttpPost("invite")]
    public async Task<IActionResult> Invite(InviteAccountRequest request)
    {
        var (user, error) = await _authService.InviteAsync(request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return CreatedAtAction(nameof(Invite), new { id = user!.Id }, user);
    }

    [HttpPost("activate-account")]
    public async Task<IActionResult> ActivateAccount(ActivateAccountRequest request)
    {
        var error = await _authService.ActivateAccountAsync(request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return NoContent();
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var (result, error) = await _authService.LoginAsync(request);
        if (error is not null)
        {
            return Unauthorized(new { message = error });
        }

        return Ok(result);
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            id = User.FindFirstValue(ClaimTypes.NameIdentifier),
            name = User.FindFirstValue(ClaimTypes.Name),
            email = User.FindFirstValue(ClaimTypes.Email),
            role = User.FindFirstValue(ClaimTypes.Role),
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request)
    {
        var (result, error) = await _authService.RefreshAsync(request);
        if (error is not null)
        {
            return Unauthorized(new { message = error });
        }

        return Ok(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshTokenRequest request)
    {
        await _authService.LogoutAsync(request);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var code = await _authService.ForgotPasswordAsync(request);
        // Respuesta genérica siempre, exista o no la cuenta, para no filtrar
        // qué correos están registrados. El código solo se expone en el
        // cuerpo mientras no exista envío de correo real (ver TODO en el
        // servicio) — en producción esto se debe quitar.
        return Ok(new
        {
            message = "Si el correo existe, se generó un código de recuperación",
            devCode = code,
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var error = await _authService.ResetPasswordAsync(request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return NoContent();
    }
}
