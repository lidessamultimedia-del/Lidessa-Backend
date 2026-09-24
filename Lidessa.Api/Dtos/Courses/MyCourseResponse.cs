namespace Lidessa.Api.Dtos.Courses;

// Curso tal como lo necesita el LMS del usuario logueado (GET api/courses/mine):
// los datos del curso más quiénes están inscritos. Al estudiante solo se le
// devuelve él mismo en Students, para no exponer a sus compañeros.
public class MyCourseResponse : CourseResponse
{
    public List<CourseStudentResponse> Students { get; set; } = new();
}

public class CourseStudentResponse
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
