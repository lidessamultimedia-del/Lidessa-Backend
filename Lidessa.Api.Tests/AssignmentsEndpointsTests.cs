using System.Net;
using System.Net.Http.Json;
using Lidessa.Api.Dtos.Assignments;
using Lidessa.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Tests;

public class AssignmentsEndpointsTests : LmsTestBase
{
    [Fact]
    public async Task Teacher_creates_assignment_in_own_course()
    {
        var response = await As(Teacher).PostAsJsonAsync($"/api/courses/{CourseId}/assignments",
            AssignmentPayload(assignedStudentIds: [Student.Id], attachmentUrl: "/uploads/guia.pdf"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<AssignmentResponse>();
        Assert.NotNull(created);
        Assert.Equal(CourseId, created.CourseId);
        Assert.Equal(TopicId, created.TopicId);
        Assert.Equal("Ensayo sobre liderazgo", created.Title);
        Assert.Equal(10.0m, created.MaxScore);
        Assert.Equal(new DateTime(2026, 11, 15, 23, 59, 0), created.DueDate);
        Assert.Equal(new DateOnly(2026, 10, 1), created.PublishAt);
        Assert.Equal("/uploads/guia.pdf", created.AttachmentUrl);
        Assert.Equal(new[] { Student.Id }, created.AssignedStudentIds);
    }

    [Fact]
    public async Task Admin_can_create_assignment_in_any_course()
    {
        var response = await As(Admin).PostAsJsonAsync($"/api/courses/{OtherCourseId}/assignments", AssignmentPayload(topicId: null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_create_assignment_in_someone_elses_course()
    {
        var response = await As(OtherTeacher).PostAsJsonAsync($"/api/courses/{CourseId}/assignments", AssignmentPayload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Student_cannot_create_assignment()
    {
        var response = await As(Student).PostAsJsonAsync($"/api/courses/{CourseId}/assignments", AssignmentPayload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var response = await Api.CreateClient().GetAsync($"/api/courses/{CourseId}/assignments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_in_missing_course_returns_404()
    {
        var response = await As(Admin).PostAsJsonAsync("/api/courses/999/assignments", AssignmentPayload(topicId: null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_topic_from_another_course_is_rejected()
    {
        var response = await As(Teacher).PostAsJsonAsync($"/api/courses/{CourseId}/assignments", AssignmentPayload(topicId: OtherCourseTopicId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_assigned_to_student_not_enrolled_is_rejected()
    {
        var response = await As(Teacher).PostAsJsonAsync($"/api/courses/{CourseId}/assignments", AssignmentPayload(assignedStudentIds: [Outsider.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_without_title_is_rejected()
    {
        var response = await As(Teacher).PostAsJsonAsync($"/api/courses/{CourseId}/assignments", AssignmentPayload(title: ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Course_assignments_are_listed_for_enrolled_users()
    {
        await CreateAssignmentAsync();
        await CreateAssignmentAsync();

        var list = await As(Student).GetFromJsonAsync<List<AssignmentResponse>>($"/api/courses/{CourseId}/assignments");

        Assert.NotNull(list);
        Assert.Equal(2, list.Count);
        Assert.All(list, a => Assert.Equal(CourseId, a.CourseId));
    }

    [Fact]
    public async Task Update_replaces_fields_and_assignees()
    {
        var id = await CreateAssignmentAsync(assignedStudentIds: [Student.Id]);

        var response = await As(Teacher).PutAsJsonAsync($"/api/assignments/{id}",
            AssignmentPayload(title: "Ensayo (v2)", topicId: null, assignedStudentIds: [Classmate.Id]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await As(Teacher).GetFromJsonAsync<AssignmentResponse>($"/api/assignments/{id}");
        Assert.Equal("Ensayo (v2)", updated!.Title);
        Assert.Null(updated.TopicId);
        Assert.Equal(new[] { Classmate.Id }, updated.AssignedStudentIds);
    }

    [Fact]
    public async Task Update_with_empty_assignees_makes_it_for_the_whole_course()
    {
        var id = await CreateAssignmentAsync(assignedStudentIds: [Student.Id]);

        await As(Teacher).PutAsJsonAsync($"/api/assignments/{id}", AssignmentPayload());

        var updated = await As(Teacher).GetFromJsonAsync<AssignmentResponse>($"/api/assignments/{id}");
        Assert.Empty(updated!.AssignedStudentIds);
    }

    [Fact]
    public async Task Teacher_cannot_update_someone_elses_assignment()
    {
        var id = await CreateAssignmentAsync();

        var response = await As(OtherTeacher).PutAsJsonAsync($"/api/assignments/{id}", AssignmentPayload());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_missing_assignment_returns_404()
    {
        var response = await As(Admin).PutAsJsonAsync("/api/assignments/999", AssignmentPayload());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_assignment()
    {
        var id = await CreateAssignmentAsync();

        var response = await As(Teacher).DeleteAsync($"/api/assignments/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(Teacher).GetAsync($"/api/assignments/{id}")).StatusCode);
        Assert.False(await Api.QueryAsync(db => db.Assignments.AnyAsync(a => a.Id == id)));
    }

    [Fact]
    public async Task Teacher_cannot_delete_someone_elses_assignment()
    {
        var id = await CreateAssignmentAsync();

        var response = await As(OtherTeacher).DeleteAsync($"/api/assignments/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await Api.QueryAsync(db => db.Assignments.AnyAsync(a => a.Id == id)));
    }
}
