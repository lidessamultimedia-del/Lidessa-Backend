using Lidessa.Api.Dtos.Certifications;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/certifications")]
[Authorize(Roles = "admin")]
public class CertificationsController : ControllerBase
{
    private readonly CertificationService _service;

    public CertificationsController(CertificationService service)
    {
        _service = service;
    }

    // Historial: los más recientes primero. Se puede filtrar por curso o estudiante.
    [HttpGet]
    public async Task<IActionResult> GetHistory([FromQuery] long? courseId, [FromQuery] long? studentId)
    {
        return Ok(await _service.GetHistoryAsync(courseId, studentId));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound(new { message = "Certificación no encontrada" }) : Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Mark(CertificationRequest request)
    {
        var (result, error) = await _service.MarkAsync(request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return CreatedAtAction(nameof(GetById), new { id = result!.Id }, result);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Unmark(long id)
    {
        var deleted = await _service.UnmarkAsync(id);
        return deleted ? NoContent() : NotFound(new { message = "Certificación no encontrada" });
    }
}
