using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class ParticipantConfiguration : IEntityTypeConfiguration<Participant>
{
    public void Configure(EntityTypeBuilder<Participant> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.FullName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Initials).IsRequired().HasMaxLength(5);
        builder.Property(p => p.ServiceCoordinator).HasMaxLength(200);
        builder.Property(p => p.GuardianName).HasMaxLength(200);
        builder.Property(p => p.GuardianPhone).HasMaxLength(50);
        builder.Property(p => p.GuardianEmail).HasMaxLength(200);
        builder.Property(p => p.ReferralSource).HasMaxLength(200);
        builder.Property(p => p.TShirtSize).HasMaxLength(20);
        // Intake notes are free-form and routinely run past 2,000 characters — nvarchar(max).
        builder.Property(p => p.IntakeNotes);
        builder.Property(p => p.EmergencyContacts).HasMaxLength(2000);
        builder.Property(p => p.Allergies).HasMaxLength(500);
        builder.Property(p => p.AreasOfConcern).HasMaxLength(1000);
        builder.Property(p => p.ServiceCoordinatorEmail).HasMaxLength(200);
        builder.Property(p => p.ServiceCoordinatorPhone).HasMaxLength(50);
        builder.Property(p => p.ContactInRemind).HasMaxLength(300);

        // Soft-deleted stars are invisible to every query, including Find and navigations.
        builder.HasQueryFilter(p => !p.IsDeleted);

        builder.HasOne(p => p.SecondaryProgram)
               .WithMany()
               .HasForeignKey(p => p.SecondaryProgramId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Program)
               .WithMany(pr => pr.Participants)
               .HasForeignKey(p => p.ProgramId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
