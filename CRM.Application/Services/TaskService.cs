using CRM.Application.DTOs.Tasks;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;
using TaskStatus = CRM.Domain.Enums.TaskStatus;

namespace CRM.Application.Services;

public class TaskService : ITaskService
{
    private readonly IUnitOfWork _uow;
    private readonly IOrgClock _clock;

    public TaskService(IUnitOfWork uow, IOrgClock clock)
    {
        _uow = uow;
        _clock = clock;
    }

    /// <summary>
    /// Overdue is derived when read, never stored (Oct 2026). The persisted IsOverdue flag was
    /// only ever set by the demo seeder, so production always showed 0 overdue tasks. A task is
    /// overdue once its due date is before the org's today and it isn't Done; a task someone
    /// explicitly moved to Overdue counts too. Due today is not overdue yet.
    /// </summary>
    public static bool IsOverdue(ProjectTask task, DateTime today) =>
        task.Status == TaskStatus.Overdue
        || (task.Status != TaskStatus.Done && task.DueDate is { } due && due.Date < today.Date);

    public async Task<IReadOnlyList<ProjectDto>> GetProjectsAsync(CancellationToken ct = default)
    {
        var projects = await _uow.Projects.GetAllAsync(ct);
        var tasks = await _uow.Tasks.GetAllAsync(ct);
        var staff = await _uow.Staff.GetAllAsync(ct);
        var staffMap = staff.ToDictionary(s => s.Id);
        var tasksByProject = tasks.GroupBy(t => t.ProjectId).ToDictionary(g => g.Key, g => g.ToList());
        var today = _clock.Today;

        return projects.Select(p => new ProjectDto
        {
            Id = p.Id,
            Title = p.Title,
            Type = p.Type,
            Status = p.Status,
            Scope = p.Scope,
            DueDate = p.DueDate?.ToString("yyyy-MM-dd"),
            Tasks = tasksByProject.GetValueOrDefault(p.Id, new())
                .Select(t => ToTaskDto(t, staffMap, today)).ToList(),
        }).ToList();
    }

    public async Task<ProjectDto> CreateProjectAsync(CreateProjectDto dto)
    {
        var project = new Project
        {
            Title = dto.Title,
            Type = dto.Type,
            Status = dto.Status,
            Scope = dto.Scope,
            DueDate = dto.DueDate,
        };

        await _uow.Projects.AddAsync(project);
        await _uow.SaveChangesAsync();

        return new ProjectDto
        {
            Id = project.Id,
            Title = project.Title,
            Type = project.Type,
            Status = project.Status,
            Scope = project.Scope,
            DueDate = project.DueDate?.ToString("yyyy-MM-dd"),
            Tasks = new(),
        };
    }

    public async Task<ProjectTaskDto> AddTaskAsync(Guid projectId, CreateTaskDto dto)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Task name is required.");

        // Check the project here (Oct 2026): a stale or wrong id used to reach the database as
        // a foreign-key violation, which read as a server fault rather than "not found".
        if (await _uow.Projects.GetByIdAsync(projectId) is null)
            throw new KeyNotFoundException("That project no longer exists. Refresh and try again.");

        // The modal's status chips (Oct 2026). Done and Overdue aren't starting states: a task is
        // finished by updating it, and overdue follows from the due date.
        var status = dto.Status ?? TaskStatus.Upcoming;
        if (status is not (TaskStatus.Upcoming or TaskStatus.InProgress or TaskStatus.Blocked))
            throw new ArgumentException("A new task can only start as Upcoming, In progress or Blocked.");

        var task = new ProjectTask
        {
            ProjectId = projectId,
            Name = name,
            Context = dto.Context,
            AssignedToId = dto.AssignedToId,
            Status = status,
            Priority = dto.Priority,
            DueDate = dto.DueDate,
        };

        await _uow.Tasks.AddAsync(task);
        await _uow.SaveChangesAsync();

        return ToTaskDto(task, await BuildStaffMapAsync(task.AssignedToId), _clock.Today);
    }

    public async Task<ProjectTaskDto?> UpdateTaskAsync(Guid taskId, UpdateTaskDto dto)
    {
        var task = await _uow.Tasks.GetByIdAsync(taskId);
        if (task is null) return null;

        if (dto.Name is not null) task.Name = dto.Name;
        if (dto.Context is not null) task.Context = dto.Context;
        if (dto.AssignedToId.HasValue) task.AssignedToId = dto.AssignedToId;
        if (dto.TaskStatus.HasValue) task.Status = dto.TaskStatus.Value;
        if (dto.Priority.HasValue) task.Priority = dto.Priority.Value;
        if (dto.DueDate.HasValue) task.DueDate = dto.DueDate;

        await _uow.Tasks.UpdateAsync(task);
        await _uow.SaveChangesAsync();

        return ToTaskDto(task, await BuildStaffMapAsync(task.AssignedToId), _clock.Today);
    }

    private async Task<Dictionary<Guid, StaffMember>> BuildStaffMapAsync(Guid? assignedToId)
    {
        if (assignedToId is null) return new();
        var member = await _uow.Staff.GetByIdAsync(assignedToId.Value);
        return member is not null ? new() { [member.Id] = member } : new();
    }

    private static ProjectTaskDto ToTaskDto(ProjectTask t, Dictionary<Guid, StaffMember> staffMap, DateTime today)
    {
        var assignee = t.AssignedToId.HasValue ? staffMap.GetValueOrDefault(t.AssignedToId.Value) : null;
        return new ProjectTaskDto
        {
            Id = t.Id,
            ProjectId = t.ProjectId,
            Name = t.Name,
            Context = t.Context,
            AssignedToId = t.AssignedToId,
            AssignedToName = assignee?.FullName,
            AssignedToInitials = assignee?.Initials,
            TaskStatus = t.Status,
            Priority = t.Priority,
            DueDate = t.DueDate?.ToString("yyyy-MM-dd"),
            IsOverdue = IsOverdue(t, today),
        };
    }
}
