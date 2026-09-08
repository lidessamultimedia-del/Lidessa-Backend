using Lidessa.Api.Dtos.DocumentTypes;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/document-types")]
public class DocumentTypesController : ControllerBase
{
    private readonly DocumentTypeService _service;

    public DocumentTypesController(DocumentTypeService service)
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
        return result is null ? NotFound(new { message = "Tipo de documento no encontrado" }) : Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Create(DocumentTypeRequest request)
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
    public async Task<IActionResult> Update(long id, DocumentTypeRequest request)
    {
        var existing = await _service.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound(new { message = "Tipo de documento no encontrado" });
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
        var existing = await _service.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound(new { message = "Tipo de documento no encontrado" });
        }

        var (deleted, error) = await _service.DeleteAsync(id);
        if (!deleted)
        {
            return BadRequest(new { message = error });
        }

        return NoContent();
    }
}
