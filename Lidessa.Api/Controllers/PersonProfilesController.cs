using Lidessa.Api.Dtos.PersonProfiles;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/person-profiles")]
[Authorize(Roles = "admin,profesor")]
public class PersonProfilesController : ControllerBase
{
    private readonly PersonProfileService _service;

    public PersonProfilesController(PersonProfileService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _service.GetAllAsync());
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound(new { message = "Perfil no encontrado" }) : Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Create(PersonProfileCreateRequest request)
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
    public async Task<IActionResult> Update(long id, PersonProfileUpdateRequest request)
    {
        var (result, error) = await _service.UpdateAsync(id, request);
        if (error is not null)
        {
            return error == "Perfil no encontrado"
                ? NotFound(new { message = error })
                : BadRequest(new { message = error });
        }

        return Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var deleted = await _service.DeleteAsync(id);
        if (!deleted)
        {
            return NotFound(new { message = "Perfil no encontrado" });
        }

        return NoContent();
    }
}
