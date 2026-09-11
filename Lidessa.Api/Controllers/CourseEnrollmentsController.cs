using Lidessa.Api.Dtos.Enrollments;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/courses/{courseId:long}/enrollments")]
[Authorize]
public class CourseEnrollmentsController : ControllerBase
{
    private readonly CourseEnrollmentService _service;

    public CourseEnrollmentsController(CourseEnrollmentService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllByCourse(long courseId)
    {
        return Ok(await _service.GetAllByCourseAsync(courseId));
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Enroll(long courseId, EnrollmentRequest request)
    {
        var (result, error) = await _service.EnrollAsync(courseId, request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return CreatedAtAction(nameof(GetAllByCourse), new { courseId }, result);
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{studentId:long}")]
    public async Task<IActionResult> Unenroll(long courseId, long studentId)
    {
        var deleted = await _service.UnenrollAsync(courseId, studentId);
        return deleted ? NoContent() : NotFound(new { message = "El estudiante no está inscrito en este curso" });
    }
}
