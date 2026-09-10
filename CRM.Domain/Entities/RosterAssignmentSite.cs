namespace CRM.Domain.Entities;

/// <summary>
/// Every site a Star attends in a term — "one Star attending two sites". The roster row
/// stays one-per-Star-per-term; this join carries the sites. <see cref="RosterAssignment.SiteId"/>
/// remains the primary (first-listed) site so everything that reads a single site keeps
/// working. Composite key, no BaseEntity — the EventSessionSite precedent.
/// </summary>
public class RosterAssignmentSite
{
    public Guid RosterAssignmentId { get; set; }
    public Guid SiteId { get; set; }

    public RosterAssignment RosterAssignment { get; set; } = null!;
    public Site Site { get; set; } = null!;
}
