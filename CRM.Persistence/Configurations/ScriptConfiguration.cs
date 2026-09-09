using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CRM.Persistence.Configurations;

public class ScriptConfiguration : IEntityTypeConfiguration<Script>
{
    public void Configure(EntityTypeBuilder<Script> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Title).IsRequired().HasMaxLength(300);
        builder.Property(s => s.Subtitle).HasMaxLength(300);
        builder.Property(s => s.Duration).HasMaxLength(50);

        // "scripts/{guid}/{guid}.pdf" is 77 characters; 200 leaves room for a layout change.
        builder.Property(s => s.PdfBlobName).HasMaxLength(200);
        // Matches the longest file name the common filesystems allow.
        builder.Property(s => s.PdfFileName).HasMaxLength(255);
    }
}
