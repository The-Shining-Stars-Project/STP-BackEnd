using CRM.Application.DTOs.Staff;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class StaffController : ControllerBase
{
    private readonly IStaffService _service;

    public StaffController(IStaffService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StaffSummaryDto>>> GetAll(CancellationToken ct) =>
        Ok(await _service.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<StaffDetailDto>> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "ManagementWrite")]
    public async Task<ActionResult<StaffDetailDto>> Create([FromBody] CreateStaffDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    public async Task<ActionResult<StaffDetailDto>> Update(Guid id, [FromBody] UpdateStaffDto dto)
    {
        var result = await _service.UpdateAsync(id, dto);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{id:guid}/onboarding/{itemId:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    public async Task<ActionResult<StaffDetailDto>> SetOnboardingItem(Guid id, Guid itemId, [FromBody] SetOnboardingItemDto dto)
    {
        var result = await _service.SetOnboardingItemAsync(id, itemId, dto);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("checklist-template")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<ChecklistTemplateItemDto>>> GetChecklistTemplate() =>
        Ok(await _service.GetChecklistTemplateAsync());

    [HttpPut("checklist-template")]
    [Authorize(Policy = "ManagementWrite")]
    public async Task<ActionResult<IReadOnlyList<ChecklistTemplateItemDto>>> UpdateChecklistTemplate([FromBody] UpdateChecklistTemplateDto dto) =>
        Ok(await _service.UpdateChecklistTemplateAsync(dto));
}
