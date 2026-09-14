using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class CalendarEventSiteConfiguration : IEntityTypeConfiguration<CalendarEventSite>
{
    public void Configure(EntityTypeBuilder<CalendarEventSite> builder)
    {
        builder.ToTable("CalendarEventSites");
        builder.HasKey(es => new { es.CalendarEventId, es.SiteId });
        // Deleting an event takes its site list with it; a site stays put while any event names it.
        builder.HasOne(es => es.CalendarEvent)
               .WithMany(e => e.Sites)
               .HasForeignKey(es => es.CalendarEventId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(es => es.Site)
               .WithMany()
               .HasForeignKey(es => es.SiteId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
