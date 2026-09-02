using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class CourseEnrollmentConfiguration : IEntityTypeConfiguration<CourseEnrollment>
{
    public void Configure(EntityTypeBuilder<CourseEnrollment> b)
    {
        b.ToTable("CourseEnrollment", "dbo");
        b.HasKey(x => new { x.CourseId, x.StudentId });

        b.Property(x => x.EnrolledAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasOne(x => x.Course).WithMany(x => x.Enrollments)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
    }
}
