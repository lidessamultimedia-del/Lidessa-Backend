using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> b)
    {
        b.ToTable("Service", "dbo", t => t.HasCheckConstraint("CK_Service_TabsJson", "ISJSON([TabsJson]) = 1"));

        b.HasKey(x => x.Id);

        b.Property(x => x.Slug).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();

        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).IsRequired().HasDefaultValue("");
        b.Property(x => x.HeroImageUrl).HasMaxLength(500);
        b.Property(x => x.Active).HasDefaultValue(true);
        b.Property(x => x.Locked).HasDefaultValue(false);
        b.Property(x => x.TabsJson).IsRequired();
        b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasIndex(x => x.CategoryId);

        b.HasOne(x => x.Category).WithMany(x => x.Services)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
