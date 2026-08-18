using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class EventAttendanceRecordConfiguration : IEntityTypeConfiguration<EventAttendanceRecord>
{
    public void Configure(EntityTypeBuilder<EventAttendanceRecord> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RowVersion).IsRowVersion();

        // ONE combined list: a Star appears at most once on a register, however many
        // programmes or sites they belong to. Mirrors the class rule on (SessionId,
        // ParticipantId) and is what makes a double-tapped "add stars" harmless.
        builder.HasIndex(r => new { r.EventSessionId, r.ParticipantId }).IsUnique();

        // Per-Star event history.
        builder.HasIndex(r => r.ParticipantId);

        builder.HasOne(r => r.EventSession)
               .WithMany(e => e.AttendanceRecords)
               .HasForeignKey(r => r.EventSessionId)
               .OnDelete(DeleteBehavior.Cascade);

        // Restrict, matching AttendanceRecord: deleting a participant must not silently erase
        // the record that they were present at a performance.
        builder.HasOne(r => r.Participant)
               .WithMany()
               .HasForeignKey(r => r.ParticipantId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Site)
               .WithMany()
               .HasForeignKey(r => r.SiteId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
