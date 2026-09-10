using System.ComponentModel.DataAnnotations;

namespace CRM.Application.DTOs.Taxonomy;

public class CreateSiteDto
{
    [Required, StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}

public class UpdateSiteDto
{
    [StringLength(150, MinimumLength = 1)]
    public string? Name { get; set; }

    public int? SortOrder { get; set; }

    /// <summary>False retires the site: it leaves every dropdown but stays on historical rosters and events.</summary>
    public bool? IsActive { get; set; }
}
