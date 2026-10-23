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
        // finished by updating it, and overdue follows from the due date. Draft is (client).
        var status = dto.Status ?? TaskStatus.Upcoming;
        if (status is not (TaskStatus.Upcoming or TaskStatus.InProgress or TaskStatus.Blocked or TaskStatus.Draft))
            throw new ArgumentException("A new task can only start as Upcoming, In progress, Blocked or Draft.");

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

        if (dto.Name is not null)
        {
            var name = dto.Name.Trim();
            if (name.Length == 0) throw new ArgumentException("Task name is required.");
            task.Name = name;
        }
        // An empty string clears the context; null leaves it alone.
        if (dto.Context is not null) task.Context = dto.Context.Trim().Length == 0 ? null : dto.Context.Trim();
        if (dto.ClearAssignee) task.AssignedToId = null;
        else if (dto.AssignedToId.HasValue) task.AssignedToId = dto.AssignedToId;
        if (dto.TaskStatus.HasValue) task.Status = dto.TaskStatus.Value;
        if (dto.Priority.HasValue) task.Priority = dto.Priority.Value;
        if (dto.ClearDueDate) task.DueDate = null;
        else if (dto.DueDate.HasValue) task.DueDate = dto.DueDate;

        await _uow.Tasks.UpdateAsync(task);
        await _uow.SaveChangesAsync();

        return ToTaskDto(task, await BuildStaffMapAsync(task.AssignedToId), _clock.Today);
    }

    public async Task<bool> DeleteTaskAsync(Guid taskId)
    {
        var task = await _uow.Tasks.GetByIdAsync(taskId);
        if (task is null) return false;
        await _uow.Tasks.DeleteAsync(task);
        await _uow.SaveChangesAsync();
        return true;
    }

    public async Task<ProjectDto?> UpdateProjectAsync(Guid projectId, UpdateProjectDto dto)
    {
        var project = await _uow.Projects.GetByIdAsync(projectId);
        if (project is null) return null;

        if (dto.Title is not null)
        {
            var title = dto.Title.Trim();
            if (title.Length == 0) throw new ArgumentException("Project title is required.");
            project.Title = title;
        }
        if (dto.Type.HasValue) project.Type = dto.Type.Value;
        if (dto.Status is not null)
        {
            var status = dto.Status.Trim().ToLowerInvariant();
            if (status.Length == 0) throw new ArgumentException("Project status is required.");
            project.Status = status;
        }
        if (dto.Scope is not null) project.Scope = dto.Scope.Trim().Length == 0 ? null : dto.Scope.Trim();
        if (dto.ClearDueDate) project.DueDate = null;
        else if (dto.DueDate.HasValue) project.DueDate = dto.DueDate;

        await _uow.Projects.UpdateAsync(project);
        await _uow.SaveChangesAsync();

        var tasks = await _uow.Tasks.ListAsync(t => t.ProjectId == projectId);
        var staff = (await _uow.Staff.GetAllAsync()).ToDictionary(s => s.Id);
        var today = _clock.Today;
        return new ProjectDto
        {
            Id = project.Id,
            Title = project.Title,
            Type = project.Type,
            Status = project.Status,
            Scope = project.Scope,
            DueDate = project.DueDate?.ToString("yyyy-MM-dd"),
            Tasks = tasks.Select(t => ToTaskDto(t, staff, today)).ToList(),
        };
    }

    public async Task<bool> DeleteProjectAsync(Guid projectId)
    {
        var project = await _uow.Projects.GetByIdAsync(projectId);
        if (project is null) return false;

        // The FK cascades in SQL, but delete the tasks here too so the behaviour doesn't depend
        // on the store (the tests run on in-memory fakes) and the audit row is honest.
        foreach (var task in await _uow.Tasks.ListAsync(t => t.ProjectId == projectId))
            await _uow.Tasks.DeleteAsync(task);
        await _uow.Projects.DeleteAsync(project);
        await _uow.SaveChangesAsync();
        return true;
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
