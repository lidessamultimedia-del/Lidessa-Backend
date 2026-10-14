using Lidessa.Api.Dtos.Services;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/services")]
public class ServicesController : ControllerBase
{
    private readonly ServicesService _service;

    public ServicesController(ServicesService service)
    {
        _service = service;
    }

    // Catálogo público de /servicios — sin login, solo los activos.
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetActive()
    {
        var all = await _service.GetAllAsync();
        return Ok(all.Where(s => s.Active));
    }

    // Panel de admin: ve también los inactivos.
    [Authorize(Roles = "admin")]
    [HttpGet("admin")]
    public async Task<IActionResult> GetAllForAdmin()
    {
        return Ok(await _service.GetAllAsync());
    }

    // Detalle público por slug (la URL de /servicios/:slug) — solo si está activo.
    [AllowAnonymous]
    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug)
    {
        var result = await _service.GetBySlugAsync(slug);
        return result is null || !result.Active ? NotFound(new { message = "Servicio no encontrado" }) : Ok(result);
    }

    // Detalle por Id para el formulario de edición del admin (ve también inactivos).
    [Authorize(Roles = "admin")]
    [HttpGet("id/{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound(new { message = "Servicio no encontrado" }) : Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Create(ServiceRequest request)
    {
        var (result, error) = await _service.CreateAsync(request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return CreatedAtAction(nameof(GetById), new { id = result!.Id }, result);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, ServiceRequest request)
    {
        if (!await _service.ExistsAsync(id))
        {
            return NotFound(new { message = "Servicio no encontrado" });
        }

        var (result, error) = await _service.UpdateAsync(id, request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var (deleted, error) = await _service.DeleteAsync(id);
        if (!deleted)
        {
            return error == "Servicio no encontrado" ? NotFound(new { message = error }) : BadRequest(new { message = error });
        }

        return NoContent();
    }
}
