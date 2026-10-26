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
        // Repeat (Oct 2026): DB defaults so rows the old API writes during the deploy gap are one-offs.
        builder.Property(t => t.RepeatFrequency).HasDefaultValue(CRM.Domain.Enums.RepeatFrequency.None);
        builder.Property(t => t.RepeatInterval).HasDefaultValue(1);
        builder.Property(t => t.RepeatWeekdays).HasDefaultValue(0);
        // Finding "did this task already spawn its next one" is a lookup by parent.
        builder.HasIndex(t => t.RecurrenceParentId);
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
