using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> b)
    {
        b.ToTable("Lesson", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.LegacyId).HasMaxLength(40);
        b.HasIndex(x => x.LegacyId).IsUnique();

        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Content).IsRequired().HasDefaultValue("");
        b.Property(x => x.SortOrder).HasDefaultValue(0);
        b.Property(x => x.AttachmentFileName).HasMaxLength(260);
        b.Property(x => x.AttachmentUrl).HasMaxLength(500);

        b.HasIndex(x => x.CourseId);
        b.HasIndex(x => x.TopicId);

        b.HasOne(x => x.Course).WithMany(x => x.Lessons)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Topic).WithMany(x => x.Lessons)
            .HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Restrict);
    }
}
