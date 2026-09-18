using System.Security.Claims;
using Lidessa.Api.Dtos.Submissions;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Authorize(Roles = "estudiante")]
public class SubmissionsController : ControllerBase
{
    private readonly SubmissionService _service;

    public SubmissionsController(SubmissionService service)
    {
        _service = service;
    }

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
