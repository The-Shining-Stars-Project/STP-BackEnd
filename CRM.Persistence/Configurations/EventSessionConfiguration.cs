using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class EventSessionConfiguration : IEntityTypeConfiguration<EventSession>
{
    public void Configure(EntityTypeBuilder<EventSession> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.Property(e => e.Title).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Venue).HasMaxLength(100);
        builder.Property(e => e.TimeRange).HasMaxLength(50);
        builder.Property(e => e.HoursLogged).HasPrecision(5, 2);

        // The events list is always read by date range.
        builder.HasIndex(e => e.Date);

        // Deliberately NOT unique. The column is nullable, and SQL Server treats NULLs as
        // equal for uniqueness — a unique index here would allow exactly one register with no
        // calendar link in the whole table.
        builder.HasIndex(e => e.CalendarEventId);

        // NOTE the absence: there is no unique index on (something, Date). Two productions can
        // share a day, which is the point — the class table's one-per-programme-per-day rule
        // exists to collapse a check-then-insert race on a DERIVED roster, and an event roster
        // is explicit, so it has no such race to guard.
    }
}
