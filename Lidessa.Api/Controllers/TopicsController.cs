using System.Security.Claims;
using Lidessa.Api.Dtos.Topics;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Authorize]
public class TopicsController : ControllerBase
{
    private readonly TopicService _service;

    public TopicsController(TopicService service)
    {
        _service = service;
    }

    [HttpGet("api/courses/{courseId:long}/topics")]
    public async Task<IActionResult> GetAllByCourse(long courseId)
    {
        if (!await _service.CourseExistsAsync(courseId))
        {
            return NotFound(new { message = "Curso no encontrado" });
        }

        return Ok(await _service.GetAllByCourseAsync(courseId));
    }

    [HttpGet("api/topics/{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound(new { message = "Tema no encontrado" }) : Ok(result);
    }

    [Authorize(Roles = "admin,profesor")]
    [HttpPost("api/courses/{courseId:long}/topics")]
    public async Task<IActionResult> Create(long courseId, TopicRequest request)
    {
        if (!await _service.CourseExistsAsync(courseId))
        {
            return NotFound(new { message = "Curso no encontrado" });
        }

        var forbidden = await CheckOwnershipAsync(courseId);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var result = await _service.CreateAsync(courseId, request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [Authorize(Roles = "admin,profesor")]
    [HttpPut("api/topics/{id:long}")]
    public async Task<IActionResult> Update(long id, TopicRequest request)
    {
        var courseId = await _service.GetTopicCourseIdAsync(id);
        if (courseId is null)
        {
            return NotFound(new { message = "Tema no encontrado" });
        }

        var forbidden = await CheckOwnershipAsync(courseId.Value);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var (result, error) = await _service.UpdateAsync(id, request);
        return error is not null ? NotFound(new { message = error }) : Ok(result);
    }

    [Authorize(Roles = "admin,profesor")]
    [HttpDelete("api/topics/{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var courseId = await _service.GetTopicCourseIdAsync(id);
        if (courseId is null)
        {
            return NotFound(new { message = "Tema no encontrado" });
        }

        var forbidden = await CheckOwnershipAsync(courseId.Value);
        if (forbidden is not null)
        {
            return forbidden;
        }

        await _service.DeleteAsync(id);
        return NoContent();
    }

    private async Task<IActionResult?> CheckOwnershipAsync(long courseId)
    {
        if (User.IsInRole("admin"))
        {
            return null;
        }

        var teacherId = await _service.GetCourseTeacherIdAsync(courseId);
        var currentUserId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (teacherId != currentUserId)
        {
            return Forbid();
        }

        return null;
    }
}
