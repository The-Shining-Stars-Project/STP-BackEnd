using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class ObjectiveAreaConfiguration : IEntityTypeConfiguration<ObjectiveArea>
{
    public void Configure(EntityTypeBuilder<ObjectiveArea> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(150);
        builder.Property(a => a.Slug).IsRequired().HasMaxLength(100);
        builder.Property(a => a.ColorHex).IsRequired().HasMaxLength(7);
        builder.HasIndex(a => a.Slug).IsUnique();
        builder.Property(a => a.AnnualGoal).HasMaxLength(1000);
        builder.Property(a => a.SixMonthBenchmark).HasMaxLength(1000);

        builder.HasMany(a => a.SubSkills)
               .WithOne(s => s.ObjectiveArea)
               .HasForeignKey(s => s.ObjectiveAreaId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
