using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class RosterAssignmentSiteConfiguration : IEntityTypeConfiguration<RosterAssignmentSite>
{
    public void Configure(EntityTypeBuilder<RosterAssignmentSite> builder)
    {
        builder.ToTable("RosterAssignmentSites");
        builder.HasKey(rs => new { rs.RosterAssignmentId, rs.SiteId });

        builder.HasOne(rs => rs.RosterAssignment)
               .WithMany(r => r.Sites)
               .HasForeignKey(rs => rs.RosterAssignmentId)
               .OnDelete(DeleteBehavior.Cascade);

        // A Site on somebody's roster must not vanish underneath it — deactivate instead.
        builder.HasOne(rs => rs.Site)
               .WithMany()
               .HasForeignKey(rs => rs.SiteId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
