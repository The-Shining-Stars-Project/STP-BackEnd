using CRM.Application.DTOs.Games;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class GamesController : ControllerBase
{
    private readonly IGameService _service;

    public GamesController(IGameService service) => _service = service;

    /// <summary>Browse/filter the Games Library. All query params optional (AND-combined).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GameSummaryDto>>> GetGames(
        [FromQuery] GameTier? tier,
        [FromQuery] Guid? objectiveAreaId,
        [FromQuery] Guid? subSkillId,
        [FromQuery] GameCategory? category,
        [FromQuery] string? q,
        [FromQuery] Guid? programId)
    {
        var games = await _service.QueryAsync(new GameFilter
        {
            Tier = tier,
            ObjectiveAreaId = objectiveAreaId,
            SubSkillId = subSkillId,
            Category = category,
            Query = q,
            ProgramId = programId,
        });
        return Ok(games);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GameDetailDto>> GetGame(Guid id)
    {
        var game = await _service.GetByIdAsync(id);
        return game is null ? NotFound() : Ok(game);
    }

    [HttpPost]
    [Authorize(Policy = "ManagementWrite")]
    public async Task<ActionResult<GameDetailDto>> CreateGame([FromBody] CreateGameDto dto)
    {
        var created = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetGame), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    public async Task<ActionResult<GameDetailDto>> UpdateGame(Guid id, [FromBody] UpdateGameDto dto)
    {
        var updated = await _service.UpdateAsync(id, dto);
        return updated is null ? NotFound() : Ok(updated);
    }
}
