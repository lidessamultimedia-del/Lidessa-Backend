using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class QuizQuestionConfiguration : IEntityTypeConfiguration<QuizQuestion>
{
    public void Configure(EntityTypeBuilder<QuizQuestion> b)
    {
        b.ToTable("QuizQuestion", "dbo", t =>
        {
            t.HasCheckConstraint("CK_QuizQuestion_Type", "[QuestionType] IN ('multiple','open')");
            t.HasCheckConstraint("CK_QuizQuestion_OptionsJson", "[OptionsJson] IS NULL OR ISJSON([OptionsJson]) = 1");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.LegacyId).HasMaxLength(40);
        b.HasIndex(x => x.LegacyId).IsUnique();

        b.Property(x => x.SortOrder).HasDefaultValue(0);
        b.Property(x => x.QuestionType).HasMaxLength(20).IsRequired().HasDefaultValue("multiple");
        b.Property(x => x.QuestionText).IsRequired();

        b.HasIndex(x => x.QuizId);

        b.HasOne(x => x.Quiz).WithMany(x => x.Questions)
            .HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
    }
}
