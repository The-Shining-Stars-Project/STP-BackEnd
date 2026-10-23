namespace CRM.Domain.Enums;

/// <summary>Stored as an int: append new values at the END, never reorder.</summary>
public enum TaskStatus
{
    Upcoming,
    InProgress,
    Done,
    Overdue,
    Blocked,
    /// <summary>Jotted down, not yet real work (client, Oct 2026). Open, but shown apart.</summary>
    Draft
}
