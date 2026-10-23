using CRM.Application.DTOs.Games;

namespace CRM.Application.Interfaces.Services;

public interface IGameBacklogService
{
    Task<IReadOnlyList<GameIdeaDto>> GetIdeasAsync();
    Task<GameIdeaDto> CreateIdeaAsync(CreateGameIdeaDto dto);
    /// <summary>Promotes an idea into a draft Games Library entry (Suggested, Multi-Area, all tiers) to be refined. Null if the idea doesn't exist.</summary>
    Task<GameIdeaDto?> PromoteIdeaAsync(Guid id);

    /// <summary>
    /// Reverses a promote (Oct 2026): deletes the library game the idea created (same rules as
    /// <see cref="IGameService.DeleteAsync"/>) and returns the idea to To Develop. Idempotent.
    /// </summary>
    Task<GameIdeaDto?> DemoteIdeaAsync(Guid id);

    Task<IReadOnlyList<AgeModificationDto>> GetAgeModsAsync();
    Task<AgeModificationDto> CreateAgeModAsync(CreateAgeModificationDto dto);
    Task<AgeModificationDto?> UpdateAgeModAsync(Guid id, UpdateAgeModificationDto dto);
    Task<bool> DeleteAgeModAsync(Guid id);
}
