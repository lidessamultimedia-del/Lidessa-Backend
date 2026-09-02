using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> b)
    {
        b.ToTable("Submission", "dbo", t =>
        {
            t.HasCheckConstraint("CK_Submission_Status", "[Status] IN ('draft','submitted','graded')");
            t.HasCheckConstraint("CK_Submission_Grade", "[Grade] IS NULL OR [Grade] BETWEEN 0 AND 10");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.LegacyId).HasMaxLength(40);
        b.HasIndex(x => x.LegacyId).IsUnique();

        b.Property(x => x.AttachmentFileName).HasMaxLength(260);
        b.Property(x => x.AttachmentUrl).HasMaxLength(500);
        b.Property(x => x.TextResponse).IsRequired().HasDefaultValue("");
        b.Property(x => x.Notes).IsRequired().HasDefaultValue("");
        b.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("draft");
        b.Property(x => x.Grade).HasPrecision(4, 1);
        b.Property(x => x.Feedback).IsRequired().HasDefaultValue("");
        b.Property(x => x.RetryAllowed).HasDefaultValue(false);
        b.Property(x => x.Seen).HasDefaultValue(false);

        b.HasIndex(x => new { x.AssignmentId, x.StudentId }).IsUnique();
        b.HasIndex(x => x.StudentId);

        b.HasOne(x => x.Assignment).WithMany(x => x.Submissions)
            .HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Cascade);
    }
}
