using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("AppUser", "dbo", t => t.HasCheckConstraint("CK_AppUser_Role", "[Role] IN ('admin','profesor','estudiante')"));

        b.HasKey(x => x.Id);

        b.Property(x => x.LegacyId).HasMaxLength(40);
        b.HasIndex(x => x.LegacyId).IsUnique();

        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();

        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.Role).HasMaxLength(20).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(30).IsRequired().HasDefaultValue("");
        b.Property(x => x.AvatarUrl).HasMaxLength(500);
        b.Property(x => x.UnreadNotifications).HasDefaultValue(0);
        b.Property(x => x.Active).HasDefaultValue(true);
        b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasOne(x => x.PersonProfile).WithOne(x => x.User)
            .HasForeignKey<PersonProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.CoursesTaught).WithOne(x => x.Teacher)
            .HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Enrollments).WithOne(x => x.Student)
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.AssignedAssignments).WithOne(x => x.Student)
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Submissions).WithOne(x => x.Student)
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.AssignedQuizzes).WithOne(x => x.Student)
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.QuizAttempts).WithOne(x => x.Student)
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.LessonProgress).WithOne(x => x.Student)
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.SentMessages).WithOne(x => x.FromUser)
            .HasForeignKey(x => x.FromUserId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.ReceivedMessages).WithOne(x => x.ToUser)
            .HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Certifications).WithOne(x => x.Student)
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.BlogPosts).WithOne(x => x.CreatedByUser)
            .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.PqrsfTickets).WithOne(x => x.Account)
            .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
