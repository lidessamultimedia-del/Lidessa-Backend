using System.Net;
using System.Net.Http.Json;
using Lidessa.Api.Dtos.Submissions;
using Lidessa.Api.Tests.Infrastructure;

namespace Lidessa.Api.Tests;

public class SubmissionsEndpointsTests : LmsTestBase
{
    private static object SubmissionPayload(bool submit, string text = "Mi respuesta") => new
    {
        textResponse = text,
        notes = "",
        attachmentFileName = "ensayo.pdf",
        attachmentUrl = "/uploads/ensayo.pdf",
        attachmentSizeBytes = 2048,
        submit,
    };

    private async Task<SubmissionResponse> SaveAsync(long assignmentId, bool submit, HttpClient? client = null)
    {
        var response = await (client ?? As(Student)).PutAsJsonAsync($"/api/assignments/{assignmentId}/submissions/me", SubmissionPayload(submit));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SubmissionResponse>())!;
    }

    private async Task<SubmissionResponse> GradeAsync(long submissionId, decimal grade, bool retryAllowed = false)
    {
        var response = await As(Teacher).PutAsJsonAsync($"/api/submissions/{submissionId}/grade", new { grade, feedback = "Bien", retryAllowed });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SubmissionResponse>())!;
    }

    // ── Estudiante: borrador y entrega ──

    [Fact]
    public async Task Get_mine_without_submission_returns_404()
    {
        var assignmentId = await CreateAssignmentAsync();

        var response = await As(Student).GetAsync($"/api/assignments/{assignmentId}/submissions/me");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Student_saves_draft_and_gets_it_back()
    {
        var assignmentId = await CreateAssignmentAsync();

        var draft = await SaveAsync(assignmentId, submit: false);
        var mine = await As(Student).GetFromJsonAsync<SubmissionResponse>($"/api/assignments/{assignmentId}/submissions/me");

        Assert.Equal("draft", draft.Status);
        Assert.Null(draft.SubmittedAt);
        Assert.Equal(draft.Id, mine!.Id);
        Assert.Equal(Student.Id, mine.StudentId);
        Assert.Equal(assignmentId, mine.AssignmentId);
        Assert.Equal("Mi respuesta", mine.TextResponse);
        Assert.Equal("/uploads/ensayo.pdf", mine.AttachmentUrl);
    }

    [Fact]
    public async Task Draft_can_be_submitted_and_reuses_the_same_submission()
    {
        var assignmentId = await CreateAssignmentAsync();

        var draft = await SaveAsync(assignmentId, submit: false);
        var submitted = await SaveAsync(assignmentId, submit: true);

        Assert.Equal(draft.Id, submitted.Id);
        Assert.Equal("submitted", submitted.Status);
        Assert.NotNull(submitted.SubmittedAt);
    }

    [Fact]
    public async Task Submitted_work_cannot_go_back_to_draft()
    {
        var assignmentId = await CreateAssignmentAsync();
        await SaveAsync(assignmentId, submit: true);

        var response = await As(Student).PutAsJsonAsync($"/api/assignments/{assignmentId}/submissions/me", SubmissionPayload(submit: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Student_not_enrolled_cannot_submit()
    {
        var assignmentId = await CreateAssignmentAsync();

        var response = await As(Outsider).PutAsJsonAsync($"/api/assignments/{assignmentId}/submissions/me", SubmissionPayload(submit: true));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Student_not_in_assignee_list_cannot_submit_or_read()
    {
        var assignmentId = await CreateAssignmentAsync(assignedStudentIds: [Student.Id]);

        var save = await As(Classmate).PutAsJsonAsync($"/api/assignments/{assignmentId}/submissions/me", SubmissionPayload(submit: true));
        var get = await As(Classmate).GetAsync($"/api/assignments/{assignmentId}/submissions/me");

        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal("submitted", (await SaveAsync(assignmentId, submit: true)).Status);
    }

    [Fact]
    public async Task Submitting_to_missing_assignment_returns_404()
    {
        var response = await As(Student).PutAsJsonAsync("/api/assignments/999/submissions/me", SubmissionPayload(submit: true));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_use_student_submission_endpoint()
    {
        var assignmentId = await CreateAssignmentAsync();

        var response = await As(Teacher).PutAsJsonAsync($"/api/assignments/{assignmentId}/submissions/me", SubmissionPayload(submit: true));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Staff: listar entregas de una tarea (endpoint nuevo) ──

    [Fact]
    public async Task Teacher_lists_all_submissions_of_an_assignment()
    {
        var assignmentId = await CreateAssignmentAsync();
        var otherAssignmentId = await CreateAssignmentAsync();
        await SaveAsync(assignmentId, submit: true);
        await SaveAsync(assignmentId, submit: false, client: As(Classmate));
        await SaveAsync(otherAssignmentId, submit: true);

        var list = await As(Teacher).GetFromJsonAsync<List<SubmissionResponse>>($"/api/assignments/{assignmentId}/submissions");

        Assert.NotNull(list);
        Assert.Equal(2, list.Count);
        Assert.All(list, s => Assert.Equal(assignmentId, s.AssignmentId));
        Assert.Equal(new[] { Student.Id, Classmate.Id }.OrderBy(x => x), list.Select(s => s.StudentId).OrderBy(x => x));
    }

    [Fact]
    public async Task Admin_lists_submissions_of_any_course()
    {
        var assignmentId = await CreateAssignmentAsync();
        await SaveAsync(assignmentId, submit: true);

        var list = await As(Admin).GetFromJsonAsync<List<SubmissionResponse>>($"/api/assignments/{assignmentId}/submissions");

        Assert.Single(list!);
    }

    [Fact]
    public async Task Listing_submissions_of_assignment_without_submissions_returns_empty()
    {
        var assignmentId = await CreateAssignmentAsync();

        var list = await As(Teacher).GetFromJsonAsync<List<SubmissionResponse>>($"/api/assignments/{assignmentId}/submissions");

        Assert.Empty(list!);
    }

    [Fact]
    public async Task Other_teacher_cannot_list_submissions()
    {
        var assignmentId = await CreateAssignmentAsync();

        var response = await As(OtherTeacher).GetAsync($"/api/assignments/{assignmentId}/submissions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Student_cannot_list_everyones_submissions()
    {
        var assignmentId = await CreateAssignmentAsync();

        var response = await As(Student).GetAsync($"/api/assignments/{assignmentId}/submissions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Listing_submissions_of_missing_assignment_returns_404()
    {
        var response = await As(Admin).GetAsync("/api/assignments/999/submissions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Calificar ──

    [Fact]
    public async Task Teacher_grades_submitted_work()
    {
        var assignmentId = await CreateAssignmentAsync();
        var submission = await SaveAsync(assignmentId, submit: true);

        var graded = await GradeAsync(submission.Id, 8.5m);

        Assert.Equal("graded", graded.Status);
        Assert.Equal(8.5m, graded.Grade);
        Assert.Equal("Bien", graded.Feedback);
        Assert.NotNull(graded.GradedAt);
        Assert.False(graded.Seen);
        Assert.False(graded.RetryAllowed);
    }

    [Fact]
    public async Task Draft_cannot_be_graded()
    {
        var assignmentId = await CreateAssignmentAsync();
        var draft = await SaveAsync(assignmentId, submit: false);

        var response = await As(Teacher).PutAsJsonAsync($"/api/submissions/{draft.Id}/grade", new { grade = 9, feedback = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.1)]
    [InlineData(10.1)]
    [InlineData(10.5)]
    public async Task Grade_out_of_range_is_rejected(decimal grade)
    {
        var assignmentId = await CreateAssignmentAsync();
        var submission = await SaveAsync(assignmentId, submit: true);

        var response = await As(Teacher).PutAsJsonAsync($"/api/submissions/{submission.Id}/grade", new { grade, feedback = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(7.9)]
    public async Task Grade_within_range_is_accepted(decimal grade)
    {
        var assignmentId = await CreateAssignmentAsync();
        var submission = await SaveAsync(assignmentId, submit: true);

        var graded = await GradeAsync(submission.Id, grade);

        Assert.Equal(grade, graded.Grade);
    }

    [Fact]
    public async Task Other_teacher_cannot_grade()
    {
        var assignmentId = await CreateAssignmentAsync();
        var submission = await SaveAsync(assignmentId, submit: true);

        var response = await As(OtherTeacher).PutAsJsonAsync($"/api/submissions/{submission.Id}/grade", new { grade = 9, feedback = "" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Graded_work_cannot_be_resubmitted_without_retry()
    {
        var assignmentId = await CreateAssignmentAsync();
        var submission = await SaveAsync(assignmentId, submit: true);
        await GradeAsync(submission.Id, 5m);

        var response = await As(Student).PutAsJsonAsync($"/api/assignments/{assignmentId}/submissions/me", SubmissionPayload(submit: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Permitir reintento (endpoint nuevo) ──

    [Fact]
    public async Task Allow_retry_reopens_graded_work_and_clears_the_grade_on_resubmit()
    {
        var assignmentId = await CreateAssignmentAsync();
        var submission = await SaveAsync(assignmentId, submit: true);
        await GradeAsync(submission.Id, 5m);

        var retryResponse = await As(Teacher).PutAsync($"/api/submissions/{submission.Id}/allow-retry", null);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var afterRetry = await retryResponse.Content.ReadFromJsonAsync<SubmissionResponse>();
        Assert.True(afterRetry!.RetryAllowed);
        Assert.Equal("graded", afterRetry.Status);
        Assert.Equal(5m, afterRetry.Grade);

        var resubmitted = await SaveAsync(assignmentId, submit: true);

        Assert.Equal(submission.Id, resubmitted.Id);
        Assert.Equal("submitted", resubmitted.Status);
        Assert.Null(resubmitted.Grade);
        Assert.Equal("", resubmitted.Feedback);
        Assert.Null(resubmitted.GradedAt);
        Assert.False(resubmitted.RetryAllowed);
    }

    [Fact]
    public async Task Retry_granted_while_grading_also_reopens_the_work()
    {
        var assignmentId = await CreateAssignmentAsync();
        var submission = await SaveAsync(assignmentId, submit: true);
        await GradeAsync(submission.Id, 5m, retryAllowed: true);

        var resubmitted = await SaveAsync(assignmentId, submit: true);

        Assert.Equal("submitted", resubmitted.Status);
    }

    [Fact]
    public async Task Other_teacher_cannot_allow_retry()
    {
        var assignmentId = await CreateAssignmentAsync();
        var submission = await SaveAsync(assignmentId, submit: true);

        var response = await As(OtherTeacher).PutAsync($"/api/submissions/{submission.Id}/allow-retry", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Student_cannot_allow_own_retry()
    {
        var assignmentId = await CreateAssignmentAsync();
        var submission = await SaveAsync(assignmentId, submit: true);

        var response = await As(Student).PutAsync($"/api/submissions/{submission.Id}/allow-retry", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Allow_retry_on_missing_submission_returns_404()
    {
        var response = await As(Admin).PutAsync("/api/submissions/999/allow-retry", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Visto ──

    [Fact]
    public async Task Student_marks_own_grade_as_seen()
    {
        var assignmentId = await CreateAssignmentAsync();
        var submission = await SaveAsync(assignmentId, submit: true);
        await GradeAsync(submission.Id, 9m);

        var response = await As(Student).PutAsync($"/api/submissions/{submission.Id}/seen", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<SubmissionResponse>())!.Seen);
    }

    [Fact]
    public async Task Student_cannot_mark_someone_elses_submission_as_seen()
    {
        var assignmentId = await CreateAssignmentAsync();
        var submission = await SaveAsync(assignmentId, submit: true);

        var response = await As(Classmate).PutAsync($"/api/submissions/{submission.Id}/seen", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
