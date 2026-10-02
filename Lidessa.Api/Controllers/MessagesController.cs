using System.Security.Claims;
using Lidessa.Api.Dtos.Messages;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly MessageService _service;

    public MessagesController(MessageService service)
    {
        _service = service;
    }

    [HttpPost("api/courses/{courseId:long}/messages")]
    public async Task<IActionResult> Send(long courseId, SendMessageRequest request)
    {
        if (!await _service.CourseExistsAsync(courseId))
        {
            return NotFound(new { message = "Curso no encontrado" });
        }

        var userId = CurrentUserId();
        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        if (!await _service.CanParticipateAsync(courseId, userId, role))
        {
            return Forbid();
        }

        var (result, error) = await _service.SendAsync(courseId, userId, role, request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Ok(result);
    }

    [HttpGet("api/courses/{courseId:long}/messages/{otherUserId:long}")]
    public async Task<IActionResult> GetThread(long courseId, long otherUserId)
    {
        var forbidden = await CheckParticipationAsync(courseId);
        if (forbidden is not null)
        {
            return forbidden;
        }

        return Ok(await _service.GetThreadAsync(courseId, CurrentUserId(), otherUserId));
    }

    [HttpPut("api/courses/{courseId:long}/messages/{otherUserId:long}/read")]
    public async Task<IActionResult> MarkThreadRead(long courseId, long otherUserId)
    {
        var forbidden = await CheckParticipationAsync(courseId);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var updated = await _service.MarkThreadReadAsync(courseId, CurrentUserId(), otherUserId);
        return Ok(new { updated });
    }

    private async Task<IActionResult?> CheckParticipationAsync(long courseId)
    {
        if (!await _service.CourseExistsAsync(courseId))
        {
            return NotFound(new { message = "Curso no encontrado" });
        }

        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return await _service.CanParticipateAsync(courseId, CurrentUserId(), role) ? null : Forbid();
    }

    private long CurrentUserId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
