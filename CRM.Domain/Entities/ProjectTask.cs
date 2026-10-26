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

    public Project Project { get; set; } = null!;
    public StaffMember? AssignedTo { get; set; }
}
