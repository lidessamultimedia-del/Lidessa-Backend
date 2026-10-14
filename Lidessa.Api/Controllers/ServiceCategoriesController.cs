using Lidessa.Api.Dtos.Services;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/service-categories")]
public class ServiceCategoriesController : ControllerBase
{
    private readonly ServiceCategoryService _service;

    public ServiceCategoriesController(ServiceCategoryService service)
    {
        _service = service;
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _service.GetAllAsync());
    }

    [Authorize]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound(new { message = "Categoría no encontrada" }) : Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Create(ServiceCategoryRequest request)
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
    public async Task<IActionResult> Update(long id, ServiceCategoryRequest request)
    {
        var (result, error) = await _service.UpdateAsync(id, request);
        if (error is not null)
        {
            return result is null && error.Contains("no encontrada") ? NotFound(new { message = error }) : BadRequest(new { message = error });
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
            return error == "Categoría no encontrada" ? NotFound(new { message = error }) : BadRequest(new { message = error });
        }

        return NoContent();
    }
}
