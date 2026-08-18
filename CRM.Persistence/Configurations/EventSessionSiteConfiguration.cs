using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class EventSessionSiteConfiguration : IEntityTypeConfiguration<EventSessionSite>
{
    public void Configure(EntityTypeBuilder<EventSessionSite> builder)
    {
        // Named explicitly: with no DbSet to pluralize from, EF would call this table
        // "EventSessionSite" while every other table in the schema is plural.
        builder.ToTable("EventSessionSites");

        builder.HasKey(es => new { es.EventSessionId, es.SiteId });

        // Cascade from the register: these rows carry no attendance data, so removing a
        // register should take its site list with it. This is NOT the case the Session→Program
        // Restrict rule protects, which is about never silently erasing an attendance ledger.
        builder.HasOne(es => es.EventSession)
               .WithMany(e => e.Sites)
               .HasForeignKey(es => es.EventSessionId)
               .OnDelete(DeleteBehavior.Cascade);

        // Restrict from the site: a Site referenced by a register must not vanish underneath it.
        builder.HasOne(es => es.Site)
               .WithMany()
               .HasForeignKey(es => es.SiteId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
