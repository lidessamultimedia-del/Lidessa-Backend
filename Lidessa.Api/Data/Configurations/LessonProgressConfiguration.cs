using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class LessonProgressConfiguration : IEntityTypeConfiguration<LessonProgress>
{
    public void Configure(EntityTypeBuilder<LessonProgress> b)
    {
        b.ToTable("LessonProgress", "dbo");
        b.HasKey(x => new { x.StudentId, x.LessonId });

        b.Property(x => x.CompletedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasOne(x => x.Course).WithMany(x => x.LessonProgress)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Lesson).WithMany(x => x.ProgressEntries)
            .HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
    }
}
