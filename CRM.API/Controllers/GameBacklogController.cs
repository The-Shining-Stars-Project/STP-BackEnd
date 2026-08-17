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
}
