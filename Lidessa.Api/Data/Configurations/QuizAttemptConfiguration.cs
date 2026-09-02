using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class QuizAttemptConfiguration : IEntityTypeConfiguration<QuizAttempt>
{
    public void Configure(EntityTypeBuilder<QuizAttempt> b)
    {
        b.ToTable("QuizAttempt", "dbo", t =>
        {
            t.HasCheckConstraint("CK_QuizAttempt_AnswersJson", "ISJSON([AnswersJson]) = 1");
            t.HasCheckConstraint("CK_QuizAttempt_Score", "[Score] BETWEEN 0 AND 10");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.LegacyId).HasMaxLength(40);
        b.HasIndex(x => x.LegacyId).IsUnique();

        b.Property(x => x.AnswersJson).IsRequired();
        b.Property(x => x.Score).HasPrecision(4, 1);
        b.Property(x => x.Feedback).IsRequired().HasDefaultValue("");
        b.Property(x => x.Reviewed).HasDefaultValue(false);
        b.Property(x => x.RetryAllowed).HasDefaultValue(false);
        b.Property(x => x.Seen).HasDefaultValue(false);
        b.Property(x => x.SubmittedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasIndex(x => new { x.QuizId, x.StudentId }).IsUnique();
        b.HasIndex(x => x.StudentId);

        b.HasOne(x => x.Quiz).WithMany(x => x.Attempts)
            .HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
    }
}
