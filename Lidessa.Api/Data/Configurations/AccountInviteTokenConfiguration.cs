using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class AccountInviteTokenConfiguration : IEntityTypeConfiguration<AccountInviteToken>
{
    public void Configure(EntityTypeBuilder<AccountInviteToken> b)
    {
        b.ToTable("AccountInviteToken", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Token).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Token).IsUnique();
        b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasOne(x => x.User).WithMany(x => x.InviteTokens)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
