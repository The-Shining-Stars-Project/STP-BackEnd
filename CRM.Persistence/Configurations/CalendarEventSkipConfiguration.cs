using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class CalendarEventSkipConfiguration : IEntityTypeConfiguration<CalendarEventSkip>
{
    public void Configure(EntityTypeBuilder<CalendarEventSkip> builder)
    {
        builder.ToTable("CalendarEventSkips");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Date).HasColumnType("date");
        // One skip per occurrence; deleting the series takes its skips with it.
        builder.HasIndex(s => new { s.CalendarEventId, s.Date }).IsUnique();
        builder.HasOne(s => s.CalendarEvent)
               .WithMany(e => e.Skips)
               .HasForeignKey(s => s.CalendarEventId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
