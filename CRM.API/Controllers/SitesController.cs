using CRM.API.Auditing;
using CRM.Application.DTOs.Taxonomy;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

/// <summary>
/// Program sites, managed from Settings. Dropdowns keep reading active sites from
/// /api/lists; this is the full list (retired included) and the write path. No delete:
/// a site is retired, because roster history and event registers point at it.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class SitesController : ControllerBase
{
    private readonly ISiteService _service;

    public SitesController(ISiteService service) => _service = service;

    // Not audited: reference data, no PII.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SiteDto>>> GetAll(CancellationToken ct) =>
        Ok(await _service.GetAllAsync(ct));

    [HttpPost]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("site.create", "Site")]
    public async Task<ActionResult<SiteDto>> Create([FromBody] CreateSiteDto dto, CancellationToken ct)
    {
        var result = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetAll), new { }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("site.update", "Site")]
    public async Task<ActionResult<SiteDto>> Update(Guid id, [FromBody] UpdateSiteDto dto, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
