namespace CRM.Domain.Entities;

/// <summary>
/// Which locations are taking part in an event — "multiple locations that we can assign".
/// Composite primary key with no BaseEntity, following the ScriptProgram join-entity
/// precedent; it carries no data of its own beyond the pairing.
/// </summary>
public class EventSessionSite
{
    public Guid EventSessionId { get; set; }
    public Guid SiteId { get; set; }

    public EventSession EventSession { get; set; } = null!;
    public Site Site { get; set; } = null!;
}
