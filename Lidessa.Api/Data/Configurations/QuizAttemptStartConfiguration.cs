using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class QuizAttemptStartConfiguration : IEntityTypeConfiguration<QuizAttemptStart>
{
    public void Configure(EntityTypeBuilder<QuizAttemptStart> b)
    {
        b.ToTable("QuizAttemptStart", "dbo");
        b.HasKey(x => new { x.QuizId, x.StudentId });

        b.Property(x => x.StartedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasOne(x => x.Quiz).WithMany()
            .HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
    }
}
