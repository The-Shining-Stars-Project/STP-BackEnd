using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class ProjectTaskConfiguration : IEntityTypeConfiguration<ProjectTask>
{
    public void Configure(EntityTypeBuilder<ProjectTask> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(300);
        builder.Property(t => t.Context).HasMaxLength(500);
        builder.Property(t => t.IsOverdue).HasDefaultValue(false);
        builder.Property(t => t.CompletionEstimated).HasDefaultValue(false);
        // CompletedByUserId deliberately has no FK (see the entity). The report reads by month.
        builder.HasIndex(t => t.CompletedAt);

        builder.HasOne(t => t.Project)
               .WithMany(p => p.Tasks)
               .HasForeignKey(t => t.ProjectId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.AssignedTo)
               .WithMany(s => s.AssignedTasks)
               .HasForeignKey(t => t.AssignedToId)
               .OnDelete(DeleteBehavior.SetNull)
               .IsRequired(false);
    }
}
