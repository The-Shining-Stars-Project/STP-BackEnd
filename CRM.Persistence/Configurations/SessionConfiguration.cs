using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.Property(s => s.Room).HasMaxLength(100);
        builder.Property(s => s.TimeRange).HasMaxLength(50);
        builder.Property(s => s.Label).HasMaxLength(200);
        builder.Property(s => s.HoursLogged).HasPrecision(5, 2);

        // One session per program per day (#9). The service normalizes Date to midnight,
        // so this reliably collapses the check-then-insert race between two teachers opening
        // the same program concurrently.
        builder.HasIndex(s => new { s.ProgramId, s.Date }).IsUnique();

        // Restrict, not Cascade: deleting a program must never silently erase the
        // attendance ledger (Sessions → AttendanceRecords). Sessions have to be
        // removed deliberately first.
        builder.HasOne(s => s.Program)
               .WithMany(p => p.Sessions)
               .HasForeignKey(s => s.ProgramId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
