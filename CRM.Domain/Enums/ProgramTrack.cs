namespace CRM.Domain.Enums;

/// <summary>
/// Which progress framework an <see cref="Entities.ObjectiveArea"/> belongs to.
/// Part-time programs use the original five-section framework; full-time Pathways
/// uses its own sections and pillars with annual goals and 6-month benchmarks.
/// </summary>
public enum ProgramTrack
{
    PartTime = 0,
    Pathways = 1,
}
