using System.Security.Claims;
using Lidessa.Api.Dtos.Lessons;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Authorize]
public class LessonsController : ControllerBase
{
    private readonly LessonService _service;
    private readonly TopicService _topicService;

    public LessonsController(LessonService service, TopicService topicService)
    {
        _service = service;
        _topicService = topicService;
    }

    [HttpGet("api/courses/{courseId:long}/lessons")]
    public async Task<IActionResult> GetAllByCourse(long courseId)
    {
        if (!await _topicService.CourseExistsAsync(courseId))
        {
            return NotFound(new { message = "Curso no encontrado" });
        }

        return Ok(await _service.GetAllByCourseAsync(courseId));
    }

    [HttpGet("api/lessons/{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound(new { message = "Lección no encontrada" }) : Ok(result);
    }

    [Authorize(Roles = "admin,profesor")]
    [HttpPost("api/courses/{courseId:long}/lessons")]
    public async Task<IActionResult> Create(long courseId, LessonRequest request)
    {
        if (!await _topicService.CourseExistsAsync(courseId))
        {
            return NotFound(new { message = "Curso no encontrado" });
        }

        var forbidden = await CheckOwnershipAsync(courseId);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var (result, error) = await _service.CreateAsync(courseId, request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return CreatedAtAction(nameof(GetById), new { id = result!.Id }, result);
    }

    [Authorize(Roles = "admin,profesor")]
    [HttpPut("api/lessons/{id:long}")]
    public async Task<IActionResult> Update(long id, LessonRequest request)
    {
        var courseId = await _service.GetLessonCourseIdAsync(id);
        if (courseId is null)
        {
            return NotFound(new { message = "Lección no encontrada" });
        }

        var forbidden = await CheckOwnershipAsync(courseId.Value);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var (result, error) = await _service.UpdateAsync(id, request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Ok(result);
    }

    [Authorize(Roles = "admin,profesor")]
    [HttpDelete("api/lessons/{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var courseId = await _service.GetLessonCourseIdAsync(id);
        if (courseId is null)
        {
            return NotFound(new { message = "Lección no encontrada" });
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

        var teacherId = await _topicService.GetCourseTeacherIdAsync(courseId);
        var currentUserId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (teacherId != currentUserId)
        {
            return Forbid();
        }

        return null;
    }
}
