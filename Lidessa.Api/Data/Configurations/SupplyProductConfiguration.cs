using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class SupplyProductConfiguration : IEntityTypeConfiguration<SupplyProduct>
{
    public void Configure(EntityTypeBuilder<SupplyProduct> b)
    {
        b.ToTable("SupplyProduct", "dbo", t =>
        {
            t.HasCheckConstraint("CK_SupplyProduct_Price", "[Price] >= 0");
            t.HasCheckConstraint("CK_SupplyProduct_Stock", "[Stock] >= 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).IsRequired().HasDefaultValue("");
        b.Property(x => x.Price).HasPrecision(14, 2);
        b.Property(x => x.ImageUrl).HasMaxLength(500);
        // Token de concurrencia: el UPDATE lleva "WHERE Stock = @original",
        // así dos pedidos simultáneos no pueden descontar del mismo stock
        // leído (sobreventa). El que pierde recibe DbUpdateConcurrencyException
        // y SupplyOrderService reintenta con el stock ya actualizado.
        b.Property(x => x.Stock).HasDefaultValue(0).IsConcurrencyToken();
        b.Property(x => x.Active).HasDefaultValue(true);
        b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
    }
}
