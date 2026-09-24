using System.Security.Claims;
using Lidessa.Api.Dtos.QuizAttempts;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Authorize]
public class QuizAttemptsController : ControllerBase
{
    private readonly QuizAttemptService _service;
    private readonly TopicService _topicService;

    public QuizAttemptsController(QuizAttemptService service, TopicService topicService)
    {
        _service = service;
        _topicService = topicService;
    }

    [Authorize(Roles = "estudiante")]
    [HttpGet("api/quizzes/{quizId:long}/attempts/me")]
    public async Task<IActionResult> GetMine(long quizId)
    {
        var forbidden = await CheckStudentAccessAsync(quizId);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var result = await _service.GetMineAsync(quizId, CurrentUserId());
        return result is null ? NotFound(new { message = "Todavía no has presentado este examen" }) : Ok(result);
    }

    [Authorize(Roles = "estudiante")]
    [HttpPost("api/quizzes/{quizId:long}/attempts/me/start")]
    public async Task<IActionResult> Start(long quizId)
    {
        var forbidden = await CheckStudentAccessAsync(quizId);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var (result, error) = await _service.StartAsync(quizId, CurrentUserId());
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Ok(result);
    }

    [Authorize(Roles = "estudiante")]
    [HttpPut("api/quizzes/{quizId:long}/attempts/me")]
    public async Task<IActionResult> Submit(long quizId, QuizAttemptRequest request)
    {
        var forbidden = await CheckStudentAccessAsync(quizId);
        if (forbidden is not null)
        {
            return forbidden;
        }

        var (result, error) = await _service.SubmitAsync(quizId, CurrentUserId(), request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return Ok(result);
    }

    [Authorize(Roles = "admin,profesor")]
    [HttpGet("api/quizzes/{quizId:long}/attempts")]
    public async Task<IActionResult> GetAllByQuiz(long quizId)
    {
        var courseId = await _service.GetQuizCourseIdAsync(quizId);
        if (courseId is null)
        {
            return NotFound(new { message = "Examen no encontrado" });
        }

        var forbidden = await CheckOwnershipAsync(courseId.Value);
        if (forbidden is not null)
        {
            return forbidden;
        }

        return Ok(await _service.GetAllByQuizAsync(quizId));
    }

    [Authorize(Roles = "admin,profesor")]
    [HttpPut("api/quiz-attempts/{id:long}/review")]
    public async Task<IActionResult> Review(long id, ReviewQuizAttemptRequest request)
    {
        var forbidden = await CheckAttemptOwnershipAsync(id);
        if (forbidden is not null)
        {
            return forbidden;
        }

        return Ok(await _service.ReviewAsync(id, request));
    }

    [Authorize(Roles = "admin,profesor")]
    [HttpPut("api/quiz-attempts/{id:long}/allow-retry")]
    public async Task<IActionResult> AllowRetry(long id)
    {
        var forbidden = await CheckAttemptOwnershipAsync(id);
        if (forbidden is not null)
        {
            return forbidden;
        }

        return Ok(await _service.AllowRetryAsync(id));
    }

    [Authorize(Roles = "estudiante")]
    [HttpPut("api/quiz-attempts/{id:long}/seen")]
    public async Task<IActionResult> MarkSeen(long id)
    {
        var result = await _service.MarkSeenAsync(id, CurrentUserId());
        return result is null ? NotFound(new { message = "Intento no encontrado" }) : Ok(result);
    }

    private async Task<IActionResult?> CheckAttemptOwnershipAsync(long attemptId)
    {
        var courseId = await _service.GetAttemptCourseIdAsync(attemptId);
        if (courseId is null)
        {
            return NotFound(new { message = "Intento no encontrado" });
        }

        return await CheckOwnershipAsync(courseId.Value);
    }

    private async Task<IActionResult?> CheckOwnershipAsync(long courseId)
    {
        if (User.IsInRole("admin"))
        {
            return null;
        }

        var teacherId = await _topicService.GetCourseTeacherIdAsync(courseId);
        if (teacherId != CurrentUserId())
        {
            return Forbid();
        }

        return null;
    }

    private async Task<IActionResult?> CheckStudentAccessAsync(long quizId)
    {
        var courseId = await _service.GetQuizCourseIdAsync(quizId);
        if (courseId is null)
        {
            return NotFound(new { message = "Examen no encontrado" });
        }

        if (!await _service.IsAssignedAsync(quizId, courseId.Value, CurrentUserId()))
        {
            return Forbid();
        }

        return null;
    }

    private long CurrentUserId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
