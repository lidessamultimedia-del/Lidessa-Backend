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

    private long CurrentUserId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
