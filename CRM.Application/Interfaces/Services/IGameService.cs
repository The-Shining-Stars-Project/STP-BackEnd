using CRM.Application.DTOs.Games;

namespace CRM.Application.Interfaces.Services;

public interface IGameService
{
    Task<IReadOnlyList<GameSummaryDto>> QueryAsync(GameFilter filter);
    Task<GameDetailDto?> GetByIdAsync(Guid id);
    Task<GameDetailDto> CreateAsync(CreateGameDto dto);
    Task<GameDetailDto?> UpdateAsync(Guid id, UpdateGameDto dto);

    /// <summary>
    /// Permanently removes a library activity (client, Oct 2026): its sub-goals go with it,
    /// modifications logged against it are kept but unlinked, and an idea promoted into it
    /// returns to To Develop. Refuses (InvalidOperationException → 409) to delete the last game:
    /// the library seeder reloads all ~57 defaults when the table is empty.
    /// </summary>
    Task<bool> DeleteAsync(Guid id);
}
