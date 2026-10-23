using CRM.Application.DTOs.Tasks;

namespace CRM.Application.Interfaces.Services;

public interface ITaskService
{
    Task<IReadOnlyList<ProjectDto>> GetProjectsAsync(CancellationToken ct = default);
    Task<ProjectDto> CreateProjectAsync(CreateProjectDto dto);
    Task<ProjectTaskDto> AddTaskAsync(Guid projectId, CreateTaskDto dto);
    Task<ProjectTaskDto?> UpdateTaskAsync(Guid taskId, UpdateTaskDto dto);

    // Oct 2026 (client: "finished tasks should disappear", "delete", "button happy"):
    // tasks and projects can be edited and removed. Deletes are permanent and management-only
    // at the controller.
    Task<bool> DeleteTaskAsync(Guid taskId);
    Task<ProjectDto?> UpdateProjectAsync(Guid projectId, UpdateProjectDto dto);
    /// <summary>Removes the project and every task in it.</summary>
    Task<bool> DeleteProjectAsync(Guid projectId);
}
