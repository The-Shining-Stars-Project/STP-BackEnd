namespace CRM.Domain.Enums;

/// <summary>
/// The job title a task is meant for (client, Oct 2026). These are the org's teaching titles,
/// not the CRM login roles in <see cref="StaffRole"/>: a "Senior Teaching Artist" signs in as a
/// Teacher. A task with a role and no assignee is open for anyone to pick up (the "grab bag"),
/// and the Completed-by report counts finished tasks per person per role for evaluations.
/// Admin is deliberately absent — teaching staff only. Stored as an int: append, never reorder.
/// </summary>
public enum TaskRole
{
    Coordinator,
    SeniorTeachingArtist,
    TeachingArtist,
    AssistantTeachingArtist,
}
