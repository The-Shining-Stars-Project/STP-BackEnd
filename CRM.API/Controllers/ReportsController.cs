using CRM.API.Auditing;
using CRM.Application.DTOs.Reports;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly IReportsService _service;

    public ReportsController(IReportsService service) => _service = service;

    /// <summary>The org-wide reporting snapshot in one request.</summary>
    // This is the server-side fetch behind the Reports page's CSV exports, so it is the
    // half of the export record that a client cannot skip. POST /api/audit/export adds the
    // detail (which file, how many rows) but is self-reported; this row is the evidence.
    [HttpGet]
    [Audited("reports.view", "Reports")]
    public async Task<ActionResult<ReportsDto>> Get(CancellationToken ct) => Ok(await _service.GetAsync(ct));
}
