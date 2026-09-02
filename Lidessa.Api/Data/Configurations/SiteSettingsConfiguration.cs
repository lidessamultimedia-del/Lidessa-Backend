using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class SiteSettingsConfiguration : IEntityTypeConfiguration<SiteSettings>
{
    public void Configure(EntityTypeBuilder<SiteSettings> b)
    {
        b.ToTable("SiteSettings", "dbo", t => t.HasCheckConstraint("CK_SiteSettings_Singleton", "[Id] = 1"));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Phone).HasMaxLength(30).IsRequired().HasDefaultValue("");
        b.Property(x => x.Email).HasMaxLength(256).IsRequired().HasDefaultValue("");
        b.Property(x => x.Address).HasMaxLength(300).IsRequired().HasDefaultValue("");
        b.Property(x => x.Schedule).HasMaxLength(200).IsRequired().HasDefaultValue("");

        b.HasData(new SiteSettings { Id = 1 });
    }
}
