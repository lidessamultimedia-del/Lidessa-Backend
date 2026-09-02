using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class PersonProfileConfiguration : IEntityTypeConfiguration<PersonProfile>
{
    public void Configure(EntityTypeBuilder<PersonProfile> b)
    {
        b.ToTable("PersonProfile", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.FirstName).HasMaxLength(100);
        b.Property(x => x.LastName).HasMaxLength(100);
        b.Property(x => x.DocumentNumber).HasMaxLength(40);
        b.Property(x => x.CourseInterest).HasMaxLength(200);
        b.Property(x => x.JoinedDate).HasDefaultValueSql("CAST(SYSUTCDATETIME() AS DATE)");

        b.HasIndex(x => x.UserId).IsUnique();

        b.HasOne(x => x.DocumentType).WithMany(x => x.PersonProfiles)
            .HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
