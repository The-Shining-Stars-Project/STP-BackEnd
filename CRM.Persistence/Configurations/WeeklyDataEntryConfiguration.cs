using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class WeeklyDataEntryConfiguration : IEntityTypeConfiguration<WeeklyDataEntry>
{
    public void Configure(EntityTypeBuilder<WeeklyDataEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.MonthKey).IsRequired().HasMaxLength(7);
        // One score per Star, per sub-skill, per week (#6). RecordWeeklyScoreAsync upserts on
        // exactly this tuple with a check-then-insert, so without the constraint a double
        // submit writes two rows — and ComputeMonthEndAsync averages them, silently skewing
        // the child's month-end progress level.
        builder.HasIndex(e => new { e.ParticipantId, e.SubSkillId, e.MonthKey, e.WeekNumber })
               .IsUnique()
               .HasDatabaseName("IX_WeeklyDataEntries_Participant_SubSkill_Month_Week");

        builder.HasIndex(e => new { e.ParticipantId, e.MonthKey });

        builder.HasOne(e => e.Participant)
               .WithMany()
               .HasForeignKey(e => e.ParticipantId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.SubSkill)
               .WithMany()
               .HasForeignKey(e => e.SubSkillId)
               .OnDelete(DeleteBehavior.Restrict);

        // Session bridge — nullable, detaches (does not delete data) if the session goes away.
        builder.HasOne<Session>()
               .WithMany()
               .HasForeignKey(e => e.SessionId)
               .OnDelete(DeleteBehavior.SetNull)
               .IsRequired(false);

        // Audit stamp — recorder link without a navigation property.
        builder.HasOne<StaffMember>()
               .WithMany()
               .HasForeignKey(e => e.RecordedByStaffMemberId)
               .OnDelete(DeleteBehavior.SetNull)
               .IsRequired(false);
    }
}
