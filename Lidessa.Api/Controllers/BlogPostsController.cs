using System.Security.Claims;
using Lidessa.Api.Dtos.BlogPosts;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/blog-posts")]
public class BlogPostsController : ControllerBase
{
    private readonly BlogPostService _service;

    public BlogPostsController(BlogPostService service)
    {
        _service = service;
    }

    // Listado público de Converge (el blog) — sin login, igual que el
    // catálogo de cursos.
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _service.GetAllAsync());
    }

    [AllowAnonymous]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound(new { message = "Publicación no encontrada" }) : Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Create(BlogPostRequest request)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _service.CreateAsync(request, userId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, BlogPostRequest request)
    {
        var result = await _service.UpdateAsync(id, request);
        return result is null ? NotFound(new { message = "Publicación no encontrada" }) : Ok(result);
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var deleted = await _service.DeleteAsync(id);
        return deleted ? NoContent() : NotFound(new { message = "Publicación no encontrada" });
    }
}
