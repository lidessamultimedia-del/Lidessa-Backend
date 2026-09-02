using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class PqrsfTicketConfiguration : IEntityTypeConfiguration<PqrsfTicket>
{
    public void Configure(EntityTypeBuilder<PqrsfTicket> b)
    {
        b.ToTable("PqrsfTicket", "dbo", t =>
        {
            t.HasCheckConstraint("CK_PqrsfTicket_Type", "[TicketType] IN ('Petición','Solicitud','Queja','Reclamo','Sugerencia','Felicitación')");
            t.HasCheckConstraint("CK_PqrsfTicket_Status", "[Status] IN ('Pendiente','Revisando','Respondida')");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TicketCode).HasMaxLength(30).IsRequired();
        b.HasIndex(x => x.TicketCode).IsUnique();

        b.Property(x => x.TicketType).HasMaxLength(20).IsRequired();
        b.Property(x => x.FromName).HasMaxLength(150).IsRequired().HasDefaultValue("Anónimo");
        b.Property(x => x.Email).HasMaxLength(256).IsRequired().HasDefaultValue("");
        b.Property(x => x.Phone).HasMaxLength(30).IsRequired().HasDefaultValue("");
        b.Property(x => x.Subject).HasMaxLength(300).IsRequired();
        b.Property(x => x.MessageBody).IsRequired().HasDefaultValue("");
        b.Property(x => x.TicketDate).HasDefaultValueSql("CAST(SYSUTCDATETIME() AS DATE)");
        b.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Pendiente");
        b.Property(x => x.EmailSent).HasDefaultValue(false);
        b.Property(x => x.IsRead).HasDefaultValue(false);
        b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasIndex(x => x.AccountId);
    }
}
