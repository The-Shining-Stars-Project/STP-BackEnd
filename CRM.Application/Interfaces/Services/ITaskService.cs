using CRM.Application.DTOs.Tasks;

namespace CRM.Application.Interfaces.Services;

public interface ITaskService
{
    Task<IReadOnlyList<ProjectDto>> GetProjectsAsync(CancellationToken ct = default);
    Task<ProjectDto> CreateProjectAsync(CreateProjectDto dto);
    Task<ProjectTaskDto> AddTaskAsync(Guid projectId, CreateTaskDto dto);
    /// <summary>
    /// Moving a task to Done records <paramref name="userId"/> as its completer and stamps the
    /// time; moving it out of Done clears both. Re-saving a task that is already Done keeps the
    /// original completer.
    /// </summary>
    Task<ProjectTaskDto?> UpdateTaskAsync(Guid userId, Guid taskId, UpdateTaskDto dto);

    /// <summary>
    /// "Take it" on a grab-bag task: assigns it to the caller's linked staff record. Throws
    /// InvalidOperationException (409) if the login has no current staff record or someone
    /// else already has it. Null if the task doesn't exist.
    /// </summary>
    Task<ProjectTaskDto?> TakeTaskAsync(Guid userId, Guid taskId);

    /// <summary>Tasks completed in an org-local calendar month, grouped per person and role.</summary>
    Task<TaskCompletionReportDto> GetCompletionsAsync(int year, int month);

    // Oct 2026 (client: "finished tasks should disappear", "delete", "button happy"):
    // tasks and projects can be edited and removed. Deletes are permanent and management-only
    // at the controller.
    Task<bool> DeleteTaskAsync(Guid taskId);
    Task<ProjectDto?> UpdateProjectAsync(Guid projectId, UpdateProjectDto dto);
    /// <summary>Removes the project and every task in it.</summary>
    Task<bool> DeleteProjectAsync(Guid projectId);

    /// <summary>
    /// "Copy to next year" (Oct 2026) for annual projects like ASCAP Reporting: a new project with
    /// every date a year later, a year in the title bumped (2026 → 2027), and each task back to
    /// Upcoming with no completer. Assignees are kept only if they're still current staff.
    /// </summary>
    Task<ProjectDto?> CopyProjectToNextYearAsync(Guid projectId);
}
