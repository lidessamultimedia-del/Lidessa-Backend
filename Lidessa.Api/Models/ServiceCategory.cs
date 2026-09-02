namespace Lidessa.Api.Models;

public class ServiceCategory
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;

    public ICollection<Service> Services { get; set; } = new List<Service>();
}
