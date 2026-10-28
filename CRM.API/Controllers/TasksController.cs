using CRM.Application.DTOs.Tasks;
using CRM.Application.Interfaces.Services;
using CRM.API.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class TasksController : ControllerBase
{
    private readonly ITaskService _service;

    public TasksController(ITaskService service) => _service = service;

    [HttpGet("projects")]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> GetProjects() =>
        Ok(await _service.GetProjectsAsync());

    // Defining org initiatives (productions, compliance drives) is management work;
    // adding/updating tasks within a project stays open to teachers (#6).
    [HttpPost("projects")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("project.create", "Project")]
    public async Task<ActionResult<ProjectDto>> CreateProject([FromBody] CreateProjectDto dto)
    {
        var result = await _service.CreateProjectAsync(dto);
        return CreatedAtAction(nameof(GetProjects), result);
    }

    // EntityType is Project, not ProjectTask: the route names the project and the new task's
    // own id is not in it, so the row identifies the container that changed.
    // The service rejects a blank name or a status a new task can't start in (400) and an
    // unknown project (404); GlobalExceptionHandler maps both, so the row records the status.
    [HttpPost("projects/{projectId:guid}/tasks")]
    [Audited("task.create", "Project", IdRouteKey = "projectId")]
    public async Task<ActionResult<ProjectTaskDto>> AddTask(Guid projectId, [FromBody] CreateTaskDto dto)
    {
        var result = await _service.AddTaskAsync(projectId, dto);
        return CreatedAtAction(nameof(GetProjects), new { }, result);
    }

    // Marking a task Done records the caller as its completer (Oct 2026, for evaluations).
    [HttpPut("tasks/{taskId:guid}")]
    [Audited("task.update", "ProjectTask", IdRouteKey = "taskId")]
    public async Task<ActionResult<ProjectTaskDto>> UpdateTask(Guid taskId, [FromBody] UpdateTaskDto dto)
    {
        var result = await _service.UpdateTaskAsync(User.GetUserId(), taskId, dto);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// "Take it" on a grab-bag task (a role, nobody assigned): any signed-in staff member can
    /// assign it to themselves. 409 if someone else got there first or the login has no staff record.
    /// </summary>
    [HttpPost("tasks/{taskId:guid}/take")]
    [Audited("task.take", "ProjectTask", IdRouteKey = "taskId")]
    public async Task<ActionResult<ProjectTaskDto>> TakeTask(Guid taskId)
    {
        var result = await _service.TakeTaskAsync(User.GetUserId(), taskId);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Who completed what in a month, per person and role — Rachel's evaluation report. Open to
    /// every signed-in staff member by client decision (Rachel, Oct 27: "it's ok if these are
    /// public, the team picks up tasks if others are absent"). Reading it is still audited.
    /// </summary>
    [HttpGet("tasks/completions")]
    [Audited("task.completions.read", "ProjectTask")]
    public async Task<ActionResult<TaskCompletionReportDto>> GetCompletions([FromQuery] int year, [FromQuery] int month) =>
        Ok(await _service.GetCompletionsAsync(year, month));

    // Deletes are permanent and management-only (client, Oct 2026: "button happy"). Teachers
    // mark a task Done instead, and Done tasks leave the default view.
    [HttpDelete("tasks/{taskId:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("task.delete", "ProjectTask", IdRouteKey = "taskId")]
    public async Task<IActionResult> DeleteTask(Guid taskId) =>
        await _service.DeleteTaskAsync(taskId) ? NoContent() : NotFound();

    [HttpPut("projects/{projectId:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("project.update", "Project", IdRouteKey = "projectId")]
    public async Task<ActionResult<ProjectDto>> UpdateProject(Guid projectId, [FromBody] UpdateProjectDto dto)
    {
        var result = await _service.UpdateProjectAsync(projectId, dto);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Copy an annual project to next year (Oct 2026): dates +1 year, tasks reset to Upcoming.</summary>
    [HttpPost("projects/{projectId:guid}/copy-to-next-year")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("project.copy", "Project", IdRouteKey = "projectId")]
    public async Task<ActionResult<ProjectDto>> CopyProjectToNextYear(Guid projectId)
    {
        var result = await _service.CopyProjectToNextYearAsync(projectId);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Removes the project and every task in it.</summary>
    [HttpDelete("projects/{projectId:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("project.delete", "Project", IdRouteKey = "projectId")]
    public async Task<IActionResult> DeleteProject(Guid projectId) =>
        await _service.DeleteProjectAsync(projectId) ? NoContent() : NotFound();
}
