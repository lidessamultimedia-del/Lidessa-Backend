using Microsoft.EntityFrameworkCore;

namespace Lidessa.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // Las entidades (AppUser, Course, etc. — ver database/schema.sql) se
    // agregan como DbSet<> mañana, junto con las clases de Models/.
}
