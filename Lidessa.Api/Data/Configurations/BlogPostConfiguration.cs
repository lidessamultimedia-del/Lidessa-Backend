using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class BlogPostConfiguration : IEntityTypeConfiguration<BlogPost>
{
    public void Configure(EntityTypeBuilder<BlogPost> b)
    {
        b.ToTable("BlogPost", "dbo");
        b.HasKey(x => x.Id);

        b.Property(x => x.LegacyId).HasMaxLength(40);
        b.HasIndex(x => x.LegacyId).IsUnique();

        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Excerpt).IsRequired().HasDefaultValue("");
        b.Property(x => x.PublishedOn).HasDefaultValueSql("CAST(SYSUTCDATETIME() AS DATE)");
        b.Property(x => x.ImageUrl).HasMaxLength(500).IsRequired();
        b.Property(x => x.Author).HasMaxLength(150).IsRequired().HasDefaultValue("");
        b.Property(x => x.Phone).HasMaxLength(30).IsRequired().HasDefaultValue("");
        b.Property(x => x.ExternalLink).HasMaxLength(500);
        b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasIndex(x => x.CreatedByUserId);
    }
}
