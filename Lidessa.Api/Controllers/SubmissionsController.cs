using System.Security.Claims;
using Lidessa.Api.Dtos.Submissions;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Authorize]
public class SubmissionsController : ControllerBase
{
    private readonly SubmissionService _service;
    private readonly TopicService _topicService;

    public SubmissionsController(SubmissionService service, TopicService topicService)
    {
        _service = service;
        _topicService = topicService;
    }

    [Authorize(Roles = "estudiante")]
    [HttpGet("api/assignments/{assignmentId:long}/submissions/me")]
    public async Task<IActionResult> GetMine(long assignmentId)
    {
        var forbidden = await CheckAccessAsync(assignmentId);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var result = await _service.GetMineAsync(assignmentId, CurrentStudentId());
        return result is null ? NotFound(new { message = "Todavía no tienes una entrega para esta tarea" }) : Ok(result);
    }

    [Authorize(Roles = "estudiante")]
    [HttpPut("api/assignments/{assignmentId:long}/submissions/me")]
    public async Task<IActionResult> Save(long assignmentId, SubmissionRequest request)
    {
        var forbidden = await CheckAccessAsync(assignmentId);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var (result, error) = await _service.SaveAsync(assignmentId, CurrentStudentId(), request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Ok(result);
    }

    [Authorize(Roles = "admin,profesor")]
    [HttpPut("api/submissions/{id:long}/grade")]
    public async Task<IActionResult> Grade(long id, GradeSubmissionRequest request)
    {
        var courseId = await _service.GetSubmissionCourseIdAsync(id);
        if (courseId is null)
        {
            return NotFound(new { message = "Entrega no encontrada" });
        }

        var forbidden = await CheckOwnershipAsync(courseId.Value);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var (result, error) = await _service.GradeAsync(id, request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Ok(result);
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

    private async Task<IActionResult?> CheckAccessAsync(long assignmentId)
    {
        var courseId = await _service.GetAssignmentCourseIdAsync(assignmentId);
        if (courseId is null)
        {
            return NotFound(new { message = "Tarea no encontrada" });
        }

        if (!await _service.IsAssignedAsync(assignmentId, courseId.Value, CurrentStudentId()))
        {
            return Forbid();
        }

        return null;
    }

    private long CurrentStudentId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
