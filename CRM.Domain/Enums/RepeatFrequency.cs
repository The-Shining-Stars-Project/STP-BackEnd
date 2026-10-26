namespace CRM.Domain.Enums;

/// <summary>
/// How a calendar event or task repeats (Oct 2026). Stored as an int: append, never reorder.
/// None (0) is the default, so every row that existed before Repeat is a one-off.
/// </summary>
public enum RepeatFrequency
{
    None,
    Daily,
    Weekly,
    Monthly,
    Yearly,
}
