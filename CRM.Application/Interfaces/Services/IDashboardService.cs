using CRM.Application.DTOs.Dashboard;

namespace CRM.Application.Interfaces.Services;

public interface IDashboardService
{
    /// <summary>
    /// Composes the full dashboard payload in a single call, scoped to the caller's
    /// programs (#1) — it aggregates participant and attendance data, so it inherits
    /// the same scoping rules as the endpoints it composes.
    /// </summary>
    Task<DashboardDto> GetAsync(Guid userId, CancellationToken ct = default);
}
