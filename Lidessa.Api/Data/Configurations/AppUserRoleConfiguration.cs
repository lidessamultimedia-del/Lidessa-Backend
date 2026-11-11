using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class AppUserRoleConfiguration : IEntityTypeConfiguration<AppUserRole>
{
    public void Configure(EntityTypeBuilder<AppUserRole> b)
    {
        b.ToTable("AppUserRole", "dbo", t => t.HasCheckConstraint("CK_AppUserRole_Role", "[Role] IN ('admin','profesor','estudiante','cliente')"));
        b.HasKey(x => x.Id);

        b.Property(x => x.Role).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.UserId, x.Role }).IsUnique();
        b.Property(x => x.GrantedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasOne(x => x.User).WithMany(x => x.Roles)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.GrantedByUser).WithMany()
            .HasForeignKey(x => x.GrantedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
