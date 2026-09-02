using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.ToTable("Message", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.LegacyId).HasMaxLength(40);
        b.HasIndex(x => x.LegacyId).IsUnique();

        b.Property(x => x.Body).IsRequired();
        b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.IsRead).HasDefaultValue(false);

        b.HasIndex(x => new { x.CourseId, x.FromUserId, x.ToUserId });
        b.HasIndex(x => x.ToUserId);

        b.HasOne(x => x.Course).WithMany(x => x.Messages)
            .HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.FromUser).WithMany(x => x.SentMessages)
            .HasForeignKey(x => x.FromUserId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ToUser).WithMany(x => x.ReceivedMessages)
            .HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
