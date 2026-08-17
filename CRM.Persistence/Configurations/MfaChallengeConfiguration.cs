using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class MfaChallengeConfiguration : IEntityTypeConfiguration<MfaChallenge>
{
    public void Configure(EntityTypeBuilder<MfaChallenge> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TokenHash).IsRequired().HasMaxLength(64);

        // The lookup path on every code submission, and unique because two challenges sharing
        // a token would make "single-use" meaningless.
        builder.HasIndex(c => c.TokenHash).IsUnique();

        // Covers the sweep that consumes a user's outstanding challenges when they restart
        // the password step — without that sweep the 5-attempt cap is bypassed by simply
        // asking for a fresh challenge.
        builder.HasIndex(c => new { c.UserId, c.ConsumedAt });

        // Cascade on purpose, unlike AuditEvent: these are ephemeral credentials, not history.
        builder.HasOne(c => c.User)
               .WithMany()
               .HasForeignKey(c => c.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(c => c.IsUsable);
    }
}
