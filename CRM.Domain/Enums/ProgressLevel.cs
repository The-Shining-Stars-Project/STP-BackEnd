namespace CRM.Domain.Enums;

/// <summary>
/// Where a Star LIVES on a skill — the developmental arc scored once per month.
/// Shared across the Games Library (game tier targeting), the weekly data tracker
/// (month-end Progress Level), and the cohort roll-up. Stored as an int; serialized
/// as a string by the global <c>JsonStringEnumConverter</c>.
/// </summary>
public enum ProgressLevel
{
    Novice,
    Intermediate,
    Expert,
    NotApplicable,

    /// <summary>Job-readiness level used by the full-time Pathways framework only.</summary>
    Vocational,
}
