using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class QuizAssigneeConfiguration : IEntityTypeConfiguration<QuizAssignee>
{
    public void Configure(EntityTypeBuilder<QuizAssignee> b)
    {
        b.ToTable("QuizAssignee", "dbo");
        b.HasKey(x => new { x.QuizId, x.StudentId });

        b.HasOne(x => x.Quiz).WithMany(x => x.Assignees)
            .HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
    }
}
