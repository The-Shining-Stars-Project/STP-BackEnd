using CRM.Application.DTOs.Dashboard;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _service;

    public DashboardController(IDashboardService service) => _service = service;

    /// <summary>The full dashboard payload in one request, scoped to the caller's programs.</summary>
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get(CancellationToken ct)
    {
        var dto = await _service.GetAsync(User.GetUserId(), ct);
        // Onboarding completion is admin-only (client rule).
        if (!User.IsInRole("Admin"))
            foreach (var s in dto.Staff) s.OnboardingProgressPct = 0;
        return Ok(dto);
    }
}
