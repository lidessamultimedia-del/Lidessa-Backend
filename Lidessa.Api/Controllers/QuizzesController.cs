using System.Security.Claims;
using Lidessa.Api.Dtos.Quizzes;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Authorize]
public class QuizzesController : ControllerBase
{
    private readonly QuizService _service;
    private readonly TopicService _topicService;

    public QuizzesController(QuizService service, TopicService topicService)
    {
        _service = service;
        _topicService = topicService;
    }

    [HttpGet("api/courses/{courseId:long}/quizzes")]
    public async Task<IActionResult> GetAllByCourse(long courseId)
    {
        if (!await _topicService.CourseExistsAsync(courseId))
        {
            return NotFound(new { message = "Curso no encontrado" });
        }

        var forbidden = await CheckReadAccessAsync(courseId);
        if (forbidden is not null)
        {
            return forbidden;
        }

        return Ok(await _service.GetAllByCourseAsync(courseId, await CanManageAsync(courseId)));
    }

    [HttpGet("api/quizzes/{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var courseId = await _service.GetQuizCourseIdAsync(id);
        if (courseId is null)
        {
            return NotFound(new { message = "Examen no encontrado" });
        }

        var forbidden = await CheckReadAccessAsync(courseId.Value);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var result = await _service.GetByIdAsync(id, await CanManageAsync(courseId.Value));
        return result is null ? NotFound(new { message = "Examen no encontrado" }) : Ok(result);
    }

    [Authorize(Roles = "admin,profesor")]
    [HttpPost("api/courses/{courseId:long}/quizzes")]
    public async Task<IActionResult> Create(long courseId, QuizRequest request)
    {
        if (!await _topicService.CourseExistsAsync(courseId))
        {
            return NotFound(new { message = "Curso no encontrado" });
        }

        if (!await CanManageAsync(courseId))
        {
            return Forbid();
        }

        var (result, error) = await _service.CreateAsync(courseId, request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return CreatedAtAction(nameof(GetById), new { id = result!.Id }, result);
    }

    [Authorize(Roles = "admin,profesor")]
    [HttpPut("api/quizzes/{id:long}")]
    public async Task<IActionResult> Update(long id, QuizRequest request)
    {
        var courseId = await _service.GetQuizCourseIdAsync(id);
        if (courseId is null)
        {
            return NotFound(new { message = "Examen no encontrado" });
        }

        if (!await CanManageAsync(courseId.Value))
        {
            return Forbid();
        }

        var (result, error) = await _service.UpdateAsync(id, request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Ok(result);
    }

    [Authorize(Roles = "admin,profesor")]
    [HttpDelete("api/quizzes/{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var courseId = await _service.GetQuizCourseIdAsync(id);
        if (courseId is null)
        {
            return NotFound(new { message = "Examen no encontrado" });
        }

        if (!await CanManageAsync(courseId.Value))
        {
            return Forbid();
        }

        await _service.DeleteAsync(id);
        return NoContent();
    }

    private async Task<IActionResult?> CheckReadAccessAsync(long courseId)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await _topicService.CanReadCourseAsync(courseId, userId, User.IsInRole("admin")))
        {
            return Forbid();
        }

        return null;
    }

    // Admin o el profesor dueño del curso: pueden editar el examen y ver
    // las respuestas correctas.
    private async Task<bool> CanManageAsync(long courseId)
    {
        if (User.IsInRole("admin"))
        {
            return true;
        }

        var teacherId = await _topicService.GetCourseTeacherIdAsync(courseId);
        var currentUserId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return teacherId == currentUserId;
    }
}
