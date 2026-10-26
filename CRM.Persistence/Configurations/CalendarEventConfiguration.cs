using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Title).IsRequired().HasMaxLength(300);
        builder.Property(e => e.Location).HasMaxLength(200);
        builder.Property(e => e.Meta).HasMaxLength(300);
        builder.Property(e => e.TimeRange).HasMaxLength(50);
        // Repeat (Oct 2026). Defaults matter: the migration runs before the new API is deployed,
        // so for a few minutes the old API inserts rows without these columns and the DB fills
        // them in — None / every 1.
        builder.Property(e => e.RepeatFrequency).HasDefaultValue(RepeatFrequency.None);
        builder.Property(e => e.RepeatInterval).HasDefaultValue(1);
        builder.Property(e => e.RepeatWeekdays).HasDefaultValue(0);

        builder.HasOne(e => e.Program)
               .WithMany(p => p.CalendarEvents)
               .HasForeignKey(e => e.ProgramId)
               .OnDelete(DeleteBehavior.SetNull)
               .IsRequired(false);
    }
}
