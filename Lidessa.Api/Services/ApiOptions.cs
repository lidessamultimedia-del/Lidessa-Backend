namespace Lidessa.Api.Services;

// URL pública del propio backend — la necesita, por ejemplo, el enlace de
// confirmación de entrega que va dentro del correo (apunta directo a una
// ruta de la API, no del frontend). Ver FrontendOptions para el caso
// contrario (enlaces que sí van al sitio).
public class ApiOptions
{
    public const string SectionName = "Api";

    public string BaseUrl { get; set; } = "http://localhost:5144";
}
