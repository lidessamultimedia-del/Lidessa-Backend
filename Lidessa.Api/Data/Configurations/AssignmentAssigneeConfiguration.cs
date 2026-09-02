using Lidessa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lidessa.Api.Data.Configurations;

public class AssignmentAssigneeConfiguration : IEntityTypeConfiguration<AssignmentAssignee>
{
    public void Configure(EntityTypeBuilder<AssignmentAssignee> b)
    {
        b.ToTable("AssignmentAssignee", "dbo");
        b.HasKey(x => new { x.AssignmentId, x.StudentId });

        b.HasOne(x => x.Assignment).WithMany(x => x.Assignees)
            .HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Cascade);
    }
}
