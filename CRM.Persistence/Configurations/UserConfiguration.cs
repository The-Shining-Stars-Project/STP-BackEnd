using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.RowVersion).IsRowVersion();

        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.FullName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(512);
        builder.Property(u => u.PasswordSalt).IsRequired().HasMaxLength(512);

        // A 20-byte secret encrypts to a 49-byte blob -> 68 base64 characters. 256 leaves
        // room for a longer secret or a future format version without a migration.
        builder.Property(u => u.MfaSecret).HasMaxLength(256);
        builder.Property(u => u.MfaEnabled).HasDefaultValue(false);
        builder.Property(u => u.LastTotpStep).HasDefaultValue(0L);

        // Defaulted so the migration can add the column to existing rows without a data fix,
        // and so a row created outside this app starts unlocked rather than at NULL.
        builder.Property(u => u.FailedMfaAttempts).HasDefaultValue(0);

        builder.HasOne(u => u.StaffMember)
               .WithMany()
               .HasForeignKey(u => u.StaffMemberId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
