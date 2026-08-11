using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).IsRequired().HasMaxLength(200);
        builder.Property(g => g.CategoryLabel).HasMaxLength(100);
        builder.Property(g => g.Description).HasMaxLength(2000);
        builder.Property(g => g.BestForVariations).HasMaxLength(1000);
        builder.Property(g => g.WhenToUse).HasMaxLength(500);
        builder.Property(g => g.Location).HasMaxLength(200);
        builder.HasIndex(g => g.Name);

        builder.HasOne(g => g.Program)
               .WithMany()
               .HasForeignKey(g => g.ProgramId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(g => g.PrimaryObjectiveArea)
               .WithMany()
               .HasForeignKey(g => g.PrimaryObjectiveAreaId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(g => g.SubGoals)
               .WithOne(sg => sg.Game)
               .HasForeignKey(sg => sg.GameId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
