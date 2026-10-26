using CRM.Domain.Common;
using CRM.Domain.Enums;
using TaskStatus = CRM.Domain.Enums.TaskStatus;

namespace CRM.Domain.Entities;

public class ProjectTask : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Context { get; set; }
    public Guid? AssignedToId { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Upcoming;
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public DateTime? DueDate { get; set; }
    public bool IsOverdue { get; set; }

    /// <summary>The job title this task is for (Oct 2026). Null = no role; see <see cref="TaskRole"/>.</summary>
    public TaskRole? Role { get; set; }

    /// <summary>
    /// The login that marked the task Done (Oct 2026, for evaluations). A plain Guid with no FK,
    /// like <see cref="AuditEvent.UserId"/>: users are hard-deleted, and deleting an account must
    /// not erase or block the record of what that person completed. Cleared if the task is reopened.
    /// </summary>
    public Guid? CompletedByUserId { get; set; }

    /// <summary>
    /// When it was marked Done, in the ORG's local time (IOrgClock.Now), not UTC: the report
    /// groups by calendar month, and a task finished at 6 pm Pacific on the 31st belongs to
    /// that month, not the next one.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// True when the completer was reconstructed from the audit log by the AddTaskRoleAndCompletion
    /// backfill (the last successful edit of a task that was already Done), not recorded at the time.
    /// </summary>
    public bool CompletionEstimated { get; set; }

    // Repeat (Oct 2026). A repeating task needs a DueDate: when it is marked Done, the next one
    // is created due on the next occurrence after this DueDate (see TaskService). Nothing is
    // generated on a timer. Same rule shape as CalendarEvent.
    public RepeatFrequency RepeatFrequency { get; set; } = RepeatFrequency.None;
    public int RepeatInterval { get; set; } = 1;
    public int RepeatWeekdays { get; set; }
    public DateTime? RepeatUntil { get; set; }
    /// <summary>
    /// The due date the rule was set on; every next task is computed from it, not from the previous
    /// task's due date, so "monthly on the 31st" goes Jan 31 → Feb 28 → Mar 31 instead of drifting
    /// to the 28th. Copied to each spawned task.
    /// </summary>
    public DateTime? RepeatAnchor { get; set; }
    /// <summary>The task whose completion created this one; also guards against spawning twice.</summary>
    public Guid? RecurrenceParentId { get; set; }

    public Project Project { get; set; } = null!;
    public StaffMember? AssignedTo { get; set; }
}
