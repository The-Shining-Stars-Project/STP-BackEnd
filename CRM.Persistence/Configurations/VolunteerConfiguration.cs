using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class VolunteerConfiguration : IEntityTypeConfiguration<Volunteer>
{
    public void Configure(EntityTypeBuilder<Volunteer> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.FullName).IsRequired().HasMaxLength(200);
        builder.Property(v => v.Initials).IsRequired().HasMaxLength(5);
        builder.Property(v => v.Phone).HasMaxLength(50);
        builder.Property(v => v.Email).HasMaxLength(200);
        builder.Property(v => v.Notes).HasMaxLength(2000);

        builder.HasQueryFilter(v => !v.IsDeleted);

        builder.HasOne(v => v.Program)
               .WithMany()
               .HasForeignKey(v => v.ProgramId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
