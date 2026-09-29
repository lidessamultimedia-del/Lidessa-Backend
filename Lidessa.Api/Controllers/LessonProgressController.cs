using System.Security.Claims;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Authorize(Roles = "estudiante")]
public class LessonProgressController : ControllerBase
{
    private readonly LessonProgressService _service;

    public LessonProgressController(LessonProgressService service)
    {
        _service = service;
    }

    [HttpPost("api/lessons/{lessonId:long}/complete")]
    public async Task<IActionResult> Complete(long lessonId)
    {
        var courseId = await _service.GetLessonCourseIdAsync(lessonId);
        if (courseId is null)
        {
            return NotFound(new { message = "Lección no encontrada" });
        }

        if (!await _service.IsEnrolledAsync(courseId.Value, CurrentStudentId()))
        {
            return Forbid();
        }

        var (result, error) = await _service.MarkLessonCompleteAsync(lessonId, courseId.Value, CurrentStudentId());
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Ok(result);
    }

    [HttpGet("api/courses/{courseId:long}/progress/me")]
    public async Task<IActionResult> GetMine(long courseId)
    {
        if (!await _service.IsEnrolledAsync(courseId, CurrentStudentId()))
        {
            return Forbid();
        }

        return Ok(await _service.GetProgressAsync(courseId, CurrentStudentId()));
    }

    private long CurrentStudentId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
