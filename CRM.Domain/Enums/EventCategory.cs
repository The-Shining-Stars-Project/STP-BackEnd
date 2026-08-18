namespace CRM.Domain.Enums;

/// <summary>
/// What kind of gathering an <see cref="Entities.EventSession"/> records attendance for.
/// A plain int column, so a further category (Rehearsal, FieldTrip) costs no schema change.
/// </summary>
public enum EventCategory
{
    Production = 0,
    Event = 1,
}
