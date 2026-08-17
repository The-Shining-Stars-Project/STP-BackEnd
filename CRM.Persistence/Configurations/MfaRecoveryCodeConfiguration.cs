using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class MfaRecoveryCodeConfiguration : IEntityTypeConfiguration<MfaRecoveryCode>
{
    public void Configure(EntityTypeBuilder<MfaRecoveryCode> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.CodeHash).IsRequired().HasMaxLength(64);

        // Deliberately NOT unique. Every lookup is "this user's code with this hash", so a
        // global unique index buys nothing — and it would turn an astronomically unlikely
        // cross-user collision into a hard failure while generating somebody's codes.
        builder.HasIndex(r => r.CodeHash);

        // The two real queries: verify an unused code, and count what is left for the
        // "you are running low" nag.
        builder.HasIndex(r => new { r.UserId, r.UsedAt });

        builder.HasOne(r => r.User)
               .WithMany()
               .HasForeignKey(r => r.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
