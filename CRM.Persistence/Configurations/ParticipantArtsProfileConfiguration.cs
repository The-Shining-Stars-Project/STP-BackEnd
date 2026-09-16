using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class ParticipantArtsProfileConfiguration : IEntityTypeConfiguration<ParticipantArtsProfile>
{
    public void Configure(EntityTypeBuilder<ParticipantArtsProfile> builder)
    {
        builder.HasKey(a => a.Id);
        // Free-form narrative — an IPP summary pasted from the regional-center document runs
        // well past 2,000 characters, and the old cap failed the save with a generic error.
        builder.Property(a => a.IppSummary);
        builder.Property(a => a.CurrentLevel);
        builder.Property(a => a.TsspArtsGoal);

        // One profile per participant.
        builder.HasIndex(a => a.ParticipantId).IsUnique();

        builder.HasOne(a => a.Participant)
               .WithOne()
               .HasForeignKey<ParticipantArtsProfile>(a => a.ParticipantId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
