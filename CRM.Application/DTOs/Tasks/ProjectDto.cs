using CRM.Domain.Enums;
using TaskStatus = CRM.Domain.Enums.TaskStatus;

namespace CRM.Application.DTOs.Tasks;

public class ProjectDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public ProjectType Type { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Scope { get; set; }
    public string? DueDate { get; set; }
    public List<ProjectTaskDto> Tasks { get; set; } = new();
}

public class ProjectTaskDto
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Context { get; set; }
    public Guid? AssignedToId { get; set; }
    public string? AssignedToName { get; set; }
    public string? AssignedToInitials { get; set; }
    public TaskStatus TaskStatus { get; set; }
    public TaskPriority Priority { get; set; }
    public string? DueDate { get; set; }

    /// <summary>Computed when read: past its due date and not Done (see <c>TaskService.IsOverdue</c>).</summary>
    public bool IsOverdue { get; set; }

    public TaskRole? Role { get; set; }
    public Guid? CompletedByUserId { get; set; }
    /// <summary>The completer's name, or "Former user" when the account has since been deleted.</summary>
    public string? CompletedByName { get; set; }
    /// <summary>Org-local time, yyyy-MM-ddTHH:mm.</summary>
    public string? CompletedAt { get; set; }
    public bool CompletionEstimated { get; set; }
}

public class CreateProjectDto
{
    public string Title { get; set; } = string.Empty;
    public ProjectType Type { get; set; } = ProjectType.Production;
    public string Status { get; set; } = "planning";
    public string? Scope { get; set; }
    public DateTime? DueDate { get; set; }
}

public class CreateTaskDto
{
    public string Name { get; set; } = string.Empty;
    public string? Context { get; set; }
    public Guid? AssignedToId { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public DateTime? DueDate { get; set; }

    /// <summary>
    /// The status chip picked in the Add task modal (Oct 2026: it was collected but never
    /// sent). Null means Upcoming. Only Upcoming, InProgress and Blocked are accepted.
    /// </summary>
    public TaskStatus? Status { get; set; }

    public TaskRole? Role { get; set; }
}

public class UpdateTaskDto
{
    public string? Name { get; set; }
    public string? Context { get; set; }
    public Guid? AssignedToId { get; set; }
    public TaskStatus? TaskStatus { get; set; }
    public TaskPriority? Priority { get; set; }
    public DateTime? DueDate { get; set; }

    /// <summary>
    /// Null fields above mean "leave alone", so unassigning or removing a due date needs an
    /// explicit flag (Oct 2026). A flag wins over a value sent in the same request.
    /// </summary>
    public bool ClearAssignee { get; set; }
    public bool ClearDueDate { get; set; }

    public TaskRole? Role { get; set; }
    public bool ClearRole { get; set; }
}

/// <summary>
/// Finished tasks for one month, per person and per role (Oct 2026, for annual evaluations).
/// Months are org-local calendar months.
/// </summary>
public class TaskCompletionReportDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public List<TaskCompletionPersonDto> People { get; set; } = new();
    public List<TaskCompletionItemDto> Items { get; set; } = new();
}

public class TaskCompletionPersonDto
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Total { get; set; }
    /// <summary>How many of Total came from the audit-log backfill rather than being recorded.</summary>
    public int Estimated { get; set; }
    /// <summary>Count per role name ("SeniorTeachingArtist", …), plus "None" for tasks without a role.</summary>
    public Dictionary<string, int> ByRole { get; set; } = new();
}

public class TaskCompletionItemDto
{
    public Guid TaskId { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public string ProjectTitle { get; set; } = string.Empty;
    public TaskRole? Role { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CompletedAt { get; set; } = string.Empty;
    public bool Estimated { get; set; }
}

public class UpdateProjectDto
{
    public string? Title { get; set; }
    public ProjectType? Type { get; set; }
    public string? Status { get; set; }
    public string? Scope { get; set; }
    public DateTime? DueDate { get; set; }
    public bool ClearDueDate { get; set; }
}
