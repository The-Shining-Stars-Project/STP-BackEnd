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
}

public class UpdateTaskDto
{
    public string? Name { get; set; }
    public string? Context { get; set; }
    public Guid? AssignedToId { get; set; }
    public TaskStatus? TaskStatus { get; set; }
    public TaskPriority? Priority { get; set; }
    public DateTime? DueDate { get; set; }
}
