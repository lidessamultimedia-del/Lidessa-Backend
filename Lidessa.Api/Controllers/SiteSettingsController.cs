using Lidessa.Api.Dtos.SiteSettings;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/site-settings")]
public class SiteSettingsController : ControllerBase
{
    private readonly SiteSettingsService _service;

    public SiteSettingsController(SiteSettingsService service)
    {
        _service = service;
    }

    // Datos de contacto y horario que muestra el footer — públicos.
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _service.GetAsync());
    }

    [Authorize(Roles = "admin")]
    [HttpPut]
    public async Task<IActionResult> Update(SiteSettingsRequest request)
    {
        return Ok(await _service.UpdateAsync(request));
    }
}
