using CRM.Application.DTOs.Games;
using CRM.Application.Interfaces.Services;
using CRM.API.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/game-backlog")]
public class GameBacklogController : ControllerBase
{
    private readonly IGameBacklogService _service;

    public GameBacklogController(IGameBacklogService service) => _service = service;

    [HttpGet("ideas")]
    public async Task<ActionResult<IReadOnlyList<GameIdeaDto>>> GetIdeas() => Ok(await _service.GetIdeasAsync());

    [HttpPost("ideas")]
    [Audited("gameidea.create", "GameIdea")]
    public async Task<ActionResult<GameIdeaDto>> CreateIdea([FromBody] CreateGameIdeaDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name is required.");
        return Ok(await _service.CreateIdeaAsync(dto));
    }

    // Promoting an idea writes into the official Games Library, whose create/update
    // endpoints are ManagementWrite — promote must match or it's a bypass (#6).
    // Proposing ideas and age-mods stays open to teachers.
    [HttpPost("ideas/{id:guid}/promote")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("gameidea.promote", "GameIdea")]
    public async Task<ActionResult<GameIdeaDto>> Promote(Guid id)
    {
        var result = await _service.PromoteIdeaAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Undoes a promote: deletes the library game it created and returns the idea to To Develop.</summary>
    [HttpPost("ideas/{id:guid}/demote")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("gameidea.demote", "GameIdea")]
    public async Task<ActionResult<GameIdeaDto>> Demote(Guid id)
    {
        var result = await _service.DemoteIdeaAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("age-mods")]
    public async Task<ActionResult<IReadOnlyList<AgeModificationDto>>> GetAgeMods() => Ok(await _service.GetAgeModsAsync());

    [HttpPost("age-mods")]
    [Audited("agemod.create", "AgeModification")]
    public async Task<ActionResult<AgeModificationDto>> CreateAgeMod([FromBody] CreateAgeModificationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.GameName) || string.IsNullOrWhiteSpace(dto.Modification))
            return BadRequest("Game name and modification are required.");
        return Ok(await _service.CreateAgeModAsync(dto));
    }

    // Editing and deleting someone's logged modification is management work (client decision
    // D14, Oct 2026); logging one stays open to teachers.
    [HttpPut("age-mods/{id:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("agemod.update", "AgeModification")]
    public async Task<ActionResult<AgeModificationDto>> UpdateAgeMod(Guid id, [FromBody] UpdateAgeModificationDto dto)
    {
        var result = await _service.UpdateAgeModAsync(id, dto);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("age-mods/{id:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("agemod.delete", "AgeModification")]
    public async Task<IActionResult> DeleteAgeMod(Guid id) =>
        await _service.DeleteAgeModAsync(id) ? NoContent() : NotFound();
}
