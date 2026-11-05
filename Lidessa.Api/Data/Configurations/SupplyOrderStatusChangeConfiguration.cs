using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class SupplyOrderStatusChangeConfiguration : IEntityTypeConfiguration<SupplyOrderStatusChange>
{
    public void Configure(EntityTypeBuilder<SupplyOrderStatusChange> b)
    {
        b.ToTable("SupplyOrderStatusChange", "dbo", t =>
            t.HasCheckConstraint("CK_SupplyOrderStatusChange_Status", "[Status] IN ('pending','confirmed','shipped','completed','rejected')"));

        b.HasKey(x => x.Id);

        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ChangedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasIndex(x => x.OrderId);

        b.HasOne(x => x.Order).WithMany(x => x.StatusHistory)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
