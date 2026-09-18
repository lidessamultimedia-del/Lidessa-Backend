using System.Security.Claims;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/files")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly AvatarService _avatarService;
    private readonly AttachmentService _attachmentService;

    public FilesController(AvatarService avatarService, AttachmentService attachmentService)
    {
        _avatarService = avatarService;
        _attachmentService = attachmentService;
    }

    [HttpPost("avatar")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadAvatar(IFormFile file)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var (result, error) = await _avatarService.UploadAvatarAsync(userId, file);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Ok(result);
    }

    // Endpoint genérico de adjuntos: lo usa el material de lección y también
    // la entrega de tareas (el estudiante sube su archivo antes de guardar
    // el Submission), sin duplicar el controlador.
    [Authorize(Roles = "admin,profesor,estudiante")]
    [HttpPost("attachments")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> UploadAttachment(IFormFile file)
    {
        var (result, error) = await _attachmentService.UploadAsync(file);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Ok(result);
    }
}
