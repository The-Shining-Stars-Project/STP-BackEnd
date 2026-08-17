using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.HasKey(e => e.Id);

        // Column lengths match the caps AuditEventFactory.Sanitize truncates to, so a value
        // that passed the sanitizer can never be rejected by the database. Getting these out
        // of step would turn a hostile user-agent string into a failed audit write, which is
        // exactly the event you would most want recorded.
        builder.Property(e => e.Action).IsRequired().HasMaxLength(64);
        builder.Property(e => e.EntityType).HasMaxLength(64);
        builder.Property(e => e.UserEmail).IsRequired().HasMaxLength(256);
        builder.Property(e => e.UserRole).HasMaxLength(32);
        builder.Property(e => e.Summary).HasMaxLength(512);
        // 45 characters is the longest possible textual IPv6 address (IPv4-mapped form).
        builder.Property(e => e.IpAddress).HasMaxLength(45);
        builder.Property(e => e.UserAgent).HasMaxLength(512);
        // Metadata is small JSON today, but it is the field that grows as new event kinds are
        // added; nvarchar(max) costs nothing until it is used.
        builder.Property(e => e.Metadata);

        // Deliberately no foreign key to User — see the comment on AuditEvent.UserId. The
        // index still makes "everything this account did" a seek rather than a scan.
        builder.HasIndex(e => e.OccurredAt).IsDescending();
        builder.HasIndex(e => new { e.UserId, e.OccurredAt });
        builder.HasIndex(e => new { e.Action, e.OccurredAt });
        builder.HasIndex(e => new { e.EntityType, e.EntityId });

        // The UserId index cannot serve the email filter, and the difference matters most in an
        // incident. A failed login against an unknown or deleted address writes UserId null and
        // only the denormalized UserEmail — so "every attempt against admin@example.org", the
        // credential-stuffing question, is exactly the lookup that has no user id to seek on.
        // Shaped like the UserId composite so it also serves the newest-first ordering.
        builder.HasIndex(e => new { e.UserEmail, e.OccurredAt });

        // No RowVersion concurrency token, unlike most entities here. Rows are inserted once
        // and never updated, so there is no second writer to conflict with.
    }
}
