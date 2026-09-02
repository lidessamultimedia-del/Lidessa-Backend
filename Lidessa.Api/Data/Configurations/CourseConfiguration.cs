using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> b)
    {
        b.ToTable("Course", "dbo", t =>
        {
            t.HasCheckConstraint("CK_Course_Format", "[Format] IN ('topics','weekly')");
            t.HasCheckConstraint("CK_Course_Modality", "[Modality] IS NULL OR [Modality] IN ('Virtual','Presencial','Semipresencial')");
            t.HasCheckConstraint("CK_Course_ObjectivesJson", "[ObjectivesJson] IS NULL OR ISJSON([ObjectivesJson]) = 1");
            t.HasCheckConstraint("CK_Course_ModulesJson", "[ModulesJson] IS NULL OR ISJSON([ModulesJson]) = 1");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.LegacyId).HasMaxLength(40);
        b.HasIndex(x => x.LegacyId).IsUnique();

        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ShortName).HasMaxLength(50).IsRequired().HasDefaultValue("");
        b.Property(x => x.Description).IsRequired().HasDefaultValue("");
        b.Property(x => x.Category).HasMaxLength(100).IsRequired().HasDefaultValue("");
        b.Property(x => x.Format).HasMaxLength(20).IsRequired().HasDefaultValue("topics");
        b.Property(x => x.Published).HasDefaultValue(false);
        b.Property(x => x.Visible).HasDefaultValue(true);
        b.Property(x => x.Listed).HasDefaultValue(false);
        b.Property(x => x.CompletionTrackingEnabled).HasDefaultValue(true);
        b.Property(x => x.RequiresPassword).HasDefaultValue(false);
        b.Property(x => x.PasswordHash).HasMaxLength(256);
        b.Property(x => x.SelfEnrollment).HasDefaultValue(false);
        b.Property(x => x.GuestAccess).HasDefaultValue(false);
        b.Property(x => x.Color).HasMaxLength(10);
        b.Property(x => x.ImageUrl).HasMaxLength(500);
        b.Property(x => x.Duration).HasMaxLength(50);
        b.Property(x => x.Modality).HasMaxLength(20);
        b.Property(x => x.Certified).HasDefaultValue(false);
        b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasIndex(x => x.TeacherId);
    }
}
