using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class SupplyOrderConfiguration : IEntityTypeConfiguration<SupplyOrder>
{
    public void Configure(EntityTypeBuilder<SupplyOrder> b)
    {
        b.ToTable("SupplyOrder", "dbo", t =>
        {
            t.HasCheckConstraint("CK_SupplyOrder_Status", "[Status] IN ('pending','confirmed','shipped','completed','rejected')");
            t.HasCheckConstraint("CK_SupplyOrder_Total", "[Total] >= 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.CustomerName).HasMaxLength(150).IsRequired();
        b.Property(x => x.CustomerPhone).HasMaxLength(30).IsRequired();
        b.Property(x => x.CustomerEmail).HasMaxLength(256).IsRequired().HasDefaultValue("");
        b.Property(x => x.Address).HasMaxLength(300).IsRequired();
        b.Property(x => x.City).HasMaxLength(100).IsRequired().HasDefaultValue("");
        b.Property(x => x.Notes).IsRequired().HasDefaultValue("");
        b.Property(x => x.Total).HasPrecision(14, 2);
        b.Property(x => x.ProofImageUrl).HasMaxLength(500);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue(SupplyOrderStatuses.Pending);
        b.Property(x => x.PaymentNoteAmount).HasPrecision(14, 2);
        b.Property(x => x.ShipmentCarrier).HasMaxLength(100);
        b.Property(x => x.ShipmentTrackingNumber).HasMaxLength(100);
        b.Property(x => x.ShipmentPhotoUrl).HasMaxLength(500);
        b.Property(x => x.RefundProofUrl).HasMaxLength(500);
        b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasIndex(x => x.ClientId);
        b.HasIndex(x => x.Status);

        // Si se borra la cuenta del cliente, el pedido queda (historial de
        // ventas) con ClientId en NULL.
        b.HasOne(x => x.Client).WithMany(x => x.SupplyOrders)
            .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.SetNull);
    }
}
