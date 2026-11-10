using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class SupplyOrderDeliveryTokenConfiguration : IEntityTypeConfiguration<SupplyOrderDeliveryToken>
{
    public void Configure(EntityTypeBuilder<SupplyOrderDeliveryToken> b)
    {
        b.ToTable("SupplyOrderDeliveryToken", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.Token).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Token).IsUnique();
        b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasOne(x => x.Order).WithMany(x => x.DeliveryTokens)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
