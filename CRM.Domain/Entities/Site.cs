using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// A physical program site (MJC Modesto, Manteca, Pathways) used by the roster and by
/// events. A first-class lookup distinct from <see cref="CrmProgram"/> so a site can host
/// several groups. Three were seeded by migration; the organisation manages the rest from
/// Settings. Sites are deactivated, never deleted — roster history and attendance point at them.
/// </summary>
public class Site : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
