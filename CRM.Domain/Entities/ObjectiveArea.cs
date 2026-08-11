using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

/// <summary>
/// One of the six colour-coded objective areas that organise every skill in the
/// program: Social, Communication, Executive Functioning, Community Integration,
/// Performing Arts, and Multi-Area. This is the shared taxonomy spine — the Games
/// Library tags games to it, the weekly tracker measures against it, and the cohort
/// roll-up aggregates by it. Reference data, seeded once via migration.
/// </summary>
public class ObjectiveArea : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    /// <summary>Which framework this area belongs to (part-time vs full-time Pathways).</summary>
    public ProgramTrack Track { get; set; } = ProgramTrack.PartTime;

    /// <summary>Pathways areas carry an annual goal and 6-month benchmark; null for part-time.</summary>
    public string? AnnualGoal { get; set; }
    public string? SixMonthBenchmark { get; set; }

    public ICollection<SubSkill> SubSkills { get; set; } = new List<SubSkill>();
}
