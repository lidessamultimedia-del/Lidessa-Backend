using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class SupplyOrderItemConfiguration : IEntityTypeConfiguration<SupplyOrderItem>
{
    public void Configure(EntityTypeBuilder<SupplyOrderItem> b)
    {
        b.ToTable("SupplyOrderItem", "dbo", t =>
        {
            t.HasCheckConstraint("CK_SupplyOrderItem_UnitPrice", "[UnitPrice] >= 0");
            t.HasCheckConstraint("CK_SupplyOrderItem_Quantity", "[Quantity] > 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
        b.Property(x => x.UnitPrice).HasPrecision(14, 2);

        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => x.ProductId);

        b.HasOne(x => x.Order).WithMany(x => x.Items)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);

        // Borrar un producto del catálogo no borra los pedidos viejos: la
        // línea conserva nombre y precio, solo pierde el vínculo.
        b.HasOne(x => x.Product).WithMany(x => x.OrderItems)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
    }
}
