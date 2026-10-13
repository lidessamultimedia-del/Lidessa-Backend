using System.Security.Claims;
using Lidessa.Api.Dtos.Pqrsf;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/pqrsf-tickets")]
public class PqrsfTicketsController : ControllerBase
{
    private readonly PqrsfTicketService _service;

    public PqrsfTicketsController(PqrsfTicketService service)
    {
        _service = service;
    }

    // Formulario público: cualquiera puede radicar, con o sin sesión. Si
    // viene con token válido, el ticket queda ligado a esa cuenta.
    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Create(PqrsfCreateRequest request)
    {
        var (result, error) = await _service.CreateAsync(request, CurrentUserIdOrNull());
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Created($"api/pqrsf-tickets/{result!.Id}", result);
    }

    [Authorize]
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine()
    {
        return Ok(await _service.GetByAccountAsync(CurrentUserIdOrNull()!.Value));
    }

    [Authorize(Roles = "admin")]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status)
    {
        return Ok(await _service.GetAllAsync(status));
    }

    [Authorize(Roles = "admin")]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound(new { message = "PQRSF no encontrado" }) : Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("{id:long}/status")]
    public async Task<IActionResult> ChangeStatus(long id, PqrsfStatusRequest request)
    {
        if (!await _service.ExistsAsync(id))
        {
            return NotFound(new { message = "PQRSF no encontrado" });
        }

        var (result, error) = await _service.ChangeStatusAsync(id, request.Status);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpPost("{id:long}/response")]
    public async Task<IActionResult> Respond(long id, PqrsfRespondRequest request)
    {
        if (!await _service.ExistsAsync(id))
        {
            return NotFound(new { message = "PQRSF no encontrado" });
        }

        var (result, error) = await _service.RespondAsync(id, request.Response);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id)
    {
        var updated = await _service.MarkReadAsync(id);
        return updated ? NoContent() : NotFound(new { message = "PQRSF no encontrado" });
    }

    private long? CurrentUserIdOrNull()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(raw, out var id) ? id : null;
    }
}
