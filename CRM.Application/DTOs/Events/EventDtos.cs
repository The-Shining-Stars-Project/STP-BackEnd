using System.ComponentModel.DataAnnotations;
using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Events;

/// <summary>One event in the list view.</summary>
public class EventSessionSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public EventCategory Category { get; set; }
    /// <summary>yyyy-MM-dd.</summary>
    public string Date { get; set; } = string.Empty;
    public string? Venue { get; set; }
    public string? TimeRange { get; set; }
    public decimal? HoursLogged { get; set; }
    public SessionStatus Status { get; set; }
    public List<EventSiteDto> Sites { get; set; } = new();
    /// <summary>Counts reflect only the Stars the caller may see — see EventRosterDto.</summary>
    public int TotalCount { get; set; }
    public int MarkedCount { get; set; }
    public int PresentCount { get; set; }
}

public class EventSiteDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// A register with its Stars. Entries are filtered to the caller's program scope: a teacher
/// assigned to one programme sees only their own Stars on a mixed register, and the counts
/// describe what they can see rather than the whole event.
/// </summary>
public class EventRosterDto
{
    public EventSessionSummaryDto Event { get; set; } = new();
    public List<EventRosterEntryDto> Entries { get; set; } = new();
}

public class EventRosterEntryDto
{
    public Guid RecordId { get; set; }
    public Guid ParticipantId { get; set; }
    public string ParticipantName { get; set; } = string.Empty;
    public string ParticipantInitials { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public string ProgramSlug { get; set; } = string.Empty;
    public Guid? SiteId { get; set; }
    public string? SiteName { get; set; }
    public AttendanceStatus Status { get; set; }
    /// <summary>False when the caller may see this Star but not mark them.</summary>
    public bool CanMark { get; set; }
}

/// <summary>A Star who could be added to the register, for the picker.</summary>
public class EventCandidateDto
{
    public Guid ParticipantId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public string ProgramSlug { get; set; } = string.Empty;
    /// <summary>True when already on the register — the picker greys these out.</summary>
    public bool AlreadyAdded { get; set; }
}

public class CreateEventSessionDto
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    public EventCategory Category { get; set; } = EventCategory.Production;

    /// <summary>yyyy-MM-dd. Normalized to midnight, matching the class-session convention.</summary>
    [Required]
    public string Date { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Venue { get; set; }

    [StringLength(50)]
    public string? TimeRange { get; set; }

    /// <summary>Which locations are taking part.</summary>
    public List<Guid> SiteIds { get; set; } = new();
}

public class UpdateEventSessionDto
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;
    public EventCategory Category { get; set; }
    [Required] public string Date { get; set; } = string.Empty;
    [StringLength(100)] public string? Venue { get; set; }
    [StringLength(50)] public string? TimeRange { get; set; }
    public decimal? HoursLogged { get; set; }
    public List<Guid> SiteIds { get; set; } = new();
}

public class AddEventParticipantsDto
{
    [Required]
    public List<Guid> ParticipantIds { get; set; } = new();

    /// <summary>Optional location to stamp on every added Star; must be one of the event's sites.</summary>
    public Guid? SiteId { get; set; }
}

public class UpdateEventRecordDto
{
    public AttendanceStatus Status { get; set; }
    public Guid? SiteId { get; set; }
}
