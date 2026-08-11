using CRM.Application.DTOs.Volunteers;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class VolunteersController : ControllerBase
{
    private readonly IVolunteerService _service;

    public VolunteersController(IVolunteerService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VolunteerDto>>> GetAll(CancellationToken ct) =>
        Ok(await _service.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VolunteerDto>> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "ManagementWrite")]
    public async Task<ActionResult<VolunteerDto>> Create([FromBody] CreateVolunteerDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    public async Task<ActionResult<VolunteerDto>> Update(Guid id, [FromBody] UpdateVolunteerDto dto)
    {
        var result = await _service.UpdateAsync(id, dto);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _service.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}
