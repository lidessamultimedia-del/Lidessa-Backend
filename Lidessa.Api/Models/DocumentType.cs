namespace Lidessa.Api.Models;

public class DocumentType
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<PersonProfile> PersonProfiles { get; set; } = new List<PersonProfile>();
}
