using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class RosterAssignmentConfiguration : IEntityTypeConfiguration<RosterAssignment>
{
    public void Configure(EntityTypeBuilder<RosterAssignment> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Notes).HasMaxLength(500);

        // One assignment per participant per enrolment per term.
        builder.HasIndex(r => new { r.ParticipantId, r.ProgramId, r.Year, r.Quarter }).IsUnique();

        builder.HasOne(r => r.Participant)
               .WithMany()
               .HasForeignKey(r => r.ParticipantId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Program)
               .WithMany()
               .HasForeignKey(r => r.ProgramId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Site)
               .WithMany()
               .HasForeignKey(r => r.SiteId)
               .OnDelete(DeleteBehavior.Restrict)
               .IsRequired(false);

        builder.HasOne(r => r.StarGroup)
               .WithMany()
               .HasForeignKey(r => r.StarGroupId)
               .OnDelete(DeleteBehavior.Restrict)
               .IsRequired(false);

        builder.HasOne(r => r.AssignedStaff)
               .WithMany()
               .HasForeignKey(r => r.AssignedStaffId)
               .OnDelete(DeleteBehavior.Restrict)
               .IsRequired(false);
    }
}
