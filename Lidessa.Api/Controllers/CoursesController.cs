using System.Security.Claims;
using Lidessa.Api.Dtos.Courses;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/courses")]
public class CoursesController : ControllerBase
{
    private readonly CourseService _service;
    private readonly TopicService _topicService;

    public CoursesController(CourseService service, TopicService topicService)
    {
        _service = service;
        _topicService = topicService;
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _service.GetAllAsync());
    }

    // Catálogo público de CEET — sin login, solo cursos listados y publicados.
    [AllowAnonymous]
    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog()
    {
        return Ok(await _service.GetCatalogAsync());
    }

    [Authorize]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var result = await _service.GetByIdAsync(id);
        if (result is null)
        {
            return NotFound(new { message = "Curso no encontrado" });
        }

        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await _topicService.CanReadCourseAsync(id, userId, User.IsInRole("admin")))
        {
            return Forbid();
        }

        return Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpGet("{id:long}/missing-for-publish")]
    public async Task<IActionResult> GetMissingForPublish(long id)
    {
        if (await _service.GetByIdAsync(id) is null)
        {
            return NotFound(new { message = "Curso no encontrado" });
        }

        var missing = await _service.GetMissingForPublishAsync(id);
        return Ok(new CourseMissingForPublishResponse { CanPublish = missing.Count == 0, Missing = missing });
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Create(CourseRequest request)
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
    public async Task<IActionResult> Update(long id, CourseRequest request)
    {
        var (result, error) = await _service.UpdateAsync(id, request);
        if (error is not null)
        {
            return error == "Curso no encontrado"
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
            return NotFound(new { message = "Curso no encontrado" });
        }

        return NoContent();
    }
}
