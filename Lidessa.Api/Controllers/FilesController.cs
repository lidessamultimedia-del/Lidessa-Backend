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

    public FilesController(AvatarService avatarService)
    {
        _avatarService = avatarService;
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
}
