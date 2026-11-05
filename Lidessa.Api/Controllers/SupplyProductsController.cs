using Lidessa.Api.Dtos.Supplies;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/supplies/products")]
public class SupplyProductsController : ControllerBase
{
    private const string NotFoundMessage = "Producto no encontrado";

    private readonly SupplyProductService _service;

    public SupplyProductsController(SupplyProductService service)
    {
        _service = service;
    }

    // Catálogo público de /tienda — sin login, solo los activos.
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetActive()
    {
        return Ok(await _service.GetAllAsync(onlyActive: true));
    }

    // Panel de Suministros del admin: ve también los ocultos.
    [Authorize(Roles = "admin")]
    [HttpGet("admin")]
    public async Task<IActionResult> GetAllForAdmin()
    {
        return Ok(await _service.GetAllAsync(onlyActive: false));
    }

    // Detalle: público si está activo; el admin ve también los ocultos.
    [AllowAnonymous]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var result = await _service.GetByIdAsync(id);
        if (result is null || (!result.Active && !User.IsInRole("admin")))
        {
            return NotFound(new { message = NotFoundMessage });
        }

        return Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Create(SupplyProductRequest request)
    {
        var result = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, SupplyProductRequest request)
    {
        var result = await _service.UpdateAsync(id, request);
        return result is null ? NotFound(new { message = NotFoundMessage }) : Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpPatch("{id:long}/toggle-active")]
    public async Task<IActionResult> ToggleActive(long id)
    {
        var result = await _service.ToggleActiveAsync(id);
        return result is null ? NotFound(new { message = NotFoundMessage }) : Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        return await _service.DeleteAsync(id) ? NoContent() : NotFound(new { message = NotFoundMessage });
    }
}
