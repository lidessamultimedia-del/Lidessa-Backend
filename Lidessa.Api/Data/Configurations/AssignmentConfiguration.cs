using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> b)
    {
        b.ToTable("Assignment", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.LegacyId).HasMaxLength(40);
        b.HasIndex(x => x.LegacyId).IsUnique();

        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).IsRequired().HasDefaultValue("");
        b.Property(x => x.MaxScore).HasPrecision(4, 1).HasDefaultValue(10.0m);
        b.Property(x => x.AttachmentFileName).HasMaxLength(260);
        b.Property(x => x.AttachmentUrl).HasMaxLength(500);

        b.HasIndex(x => x.CourseId);

        b.HasOne(x => x.Course).WithMany(x => x.Assignments)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Topic).WithMany(x => x.Assignments)
            .HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Restrict);
    }
}
