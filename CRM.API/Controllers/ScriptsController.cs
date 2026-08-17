using CRM.Application.DTOs.Scripts;
using CRM.Application.Interfaces.Services;
using CRM.API.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ScriptsController : ControllerBase
{
    private readonly IScriptService _service;

    public ScriptsController(IScriptService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScriptDto>>> GetAll() =>
        Ok(await _service.GetAllAsync());

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScriptDto>> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("script.create", "Script")]
    public async Task<ActionResult<ScriptDto>> Create([FromBody] CreateScriptDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("script.update", "Script")]
    public async Task<ActionResult<ScriptDto>> Update(Guid id, [FromBody] UpdateScriptDto dto)
    {
        var result = await _service.UpdateAsync(id, dto);
        return result is null ? NotFound() : Ok(result);
    }
}
