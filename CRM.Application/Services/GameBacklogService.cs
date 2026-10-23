using CRM.Application.DTOs.Games;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Application.Services;

public class GameBacklogService : IGameBacklogService
{
    private readonly IUnitOfWork _uow;
    private readonly IGameService _games;

    public GameBacklogService(IUnitOfWork uow, IGameService games)
    {
        _uow = uow;
        _games = games;
    }

    public async Task<IReadOnlyList<GameIdeaDto>> GetIdeasAsync()
    {
        var ideas = await _uow.GameIdeas.GetAllAsync();
        var staff = await StaffNameMapAsync();
        return ideas.OrderBy(i => i.Name).Select(i => ToIdeaDto(i, staff)).ToList();
    }

    public async Task<GameIdeaDto> CreateIdeaAsync(CreateGameIdeaDto dto)
    {
        var idea = new GameIdea
        {
            Name = dto.Name,
            StatusNotes = dto.StatusNotes,
            SourceInspiration = dto.SourceInspiration,
            TargetCategory = dto.TargetCategory,
            TeacherSuggested = dto.TeacherSuggested,
            TeacherSuggestedId = dto.TeacherSuggestedId,
        };
        await _uow.GameIdeas.AddAsync(idea);
        await _uow.SaveChangesAsync();
        return ToIdeaDto(idea, await StaffNameMapAsync());
    }

    public async Task<GameIdeaDto?> PromoteIdeaAsync(Guid id)
    {
        var idea = await _uow.GameIdeas.GetByIdAsync(id);
        if (idea is null) return null;
        if (idea.PromotedGameId is not null) return ToIdeaDto(idea, await StaffNameMapAsync()); // already promoted

        // Draft library entry — Multi-Area / all tiers; refine it in the Games editor.
        var areas = await _uow.ObjectiveAreas.GetAllAsync();
        var areaId = (areas.FirstOrDefault(a => a.Slug == "multi-area") ?? areas.First()).Id;

        var game = new Game
        {
            Name = idea.Name,
            Source = GameSource.Suggested,
            Category = idea.TargetCategory ?? GameCategory.SuggestedAddition,
            Tiers = GameTier.All,
            PrimaryObjectiveAreaId = areaId,
            Description = idea.StatusNotes,
        };
        await _uow.Games.AddAsync(game);
        await _uow.SaveChangesAsync();

        idea.PromotedGameId = game.Id;
        await _uow.GameIdeas.UpdateAsync(idea);
        await _uow.SaveChangesAsync();

        return ToIdeaDto(idea, await StaffNameMapAsync());
    }

    public async Task<GameIdeaDto?> DemoteIdeaAsync(Guid id)
    {
        var idea = await _uow.GameIdeas.GetByIdAsync(id);
        if (idea is null) return null;
        if (idea.PromotedGameId is not { } gameId) return ToIdeaDto(idea, await StaffNameMapAsync()); // never promoted

        // GameService.DeleteAsync already clears PromotedGameId on every idea pointing at the
        // game; the explicit clear below covers a game that was deleted some other way.
        await _games.DeleteAsync(gameId);

        idea.PromotedGameId = null;
        await _uow.GameIdeas.UpdateAsync(idea);
        await _uow.SaveChangesAsync();
        return ToIdeaDto(idea, await StaffNameMapAsync());
    }

    public async Task<IReadOnlyList<AgeModificationDto>> GetAgeModsAsync()
    {
        var mods = await _uow.AgeModifications.GetAllAsync();
        var staff = await StaffNameMapAsync();
        return mods.OrderByDescending(m => m.CreatedAt).Select(m => ToModDto(m, staff)).ToList();
    }

    public async Task<AgeModificationDto?> UpdateAgeModAsync(Guid id, UpdateAgeModificationDto dto)
    {
        var mod = await _uow.AgeModifications.GetByIdAsync(id);
        if (mod is null) return null;

        var gameName = dto.GameName.Trim();
        var modification = dto.Modification.Trim();
        if (gameName.Length == 0 || modification.Length == 0)
            throw new ArgumentException("Game name and modification are required.");
        if (dto.GameId is { } gameId && await _uow.Games.GetByIdAsync(gameId) is null)
            throw new ArgumentException("That activity no longer exists in the library.");

        mod.GameName = gameName;
        mod.GroupAgeLevel = dto.GroupAgeLevel.Trim();
        mod.Modification = modification;
        mod.GameId = dto.GameId;
        await _uow.AgeModifications.UpdateAsync(mod);
        await _uow.SaveChangesAsync();
        return ToModDto(mod, await StaffNameMapAsync());
    }

    public async Task<bool> DeleteAgeModAsync(Guid id)
    {
        var mod = await _uow.AgeModifications.GetByIdAsync(id);
        if (mod is null) return false;
        await _uow.AgeModifications.DeleteAsync(mod);
        await _uow.SaveChangesAsync();
        return true;
    }

    private static AgeModificationDto ToModDto(AgeModification m, Dictionary<Guid, string> staff) => new()
    {
        Id = m.Id,
        GameName = m.GameName,
        GroupAgeLevel = m.GroupAgeLevel,
        Modification = m.Modification,
        TeacherSuggested = m.TeacherSuggested,
        TeacherSuggestedId = m.TeacherSuggestedId,
        TeacherSuggestedName = m.TeacherSuggestedId is { } sid ? staff.GetValueOrDefault(sid) : null,
        GameId = m.GameId,
    };

    public async Task<AgeModificationDto> CreateAgeModAsync(CreateAgeModificationDto dto)
    {
        var mod = new AgeModification
        {
            GameName = dto.GameName,
            GroupAgeLevel = dto.GroupAgeLevel,
            Modification = dto.Modification,
            TeacherSuggested = dto.TeacherSuggested,
            TeacherSuggestedId = dto.TeacherSuggestedId,
            GameId = dto.GameId,
        };
        await _uow.AgeModifications.AddAsync(mod);
        await _uow.SaveChangesAsync();
        return ToModDto(mod, await StaffNameMapAsync());
    }

    private async Task<Dictionary<Guid, string>> StaffNameMapAsync() =>
        (await _uow.Staff.GetAllAsync()).ToDictionary(s => s.Id, s => s.FullName);

    private static GameIdeaDto ToIdeaDto(GameIdea i, Dictionary<Guid, string> staff) => new()
    {
        Id = i.Id,
        Name = i.Name,
        StatusNotes = i.StatusNotes,
        SourceInspiration = i.SourceInspiration,
        TargetCategory = i.TargetCategory,
        TeacherSuggested = i.TeacherSuggested,
        TeacherSuggestedId = i.TeacherSuggestedId,
        TeacherSuggestedName = i.TeacherSuggestedId is { } sid ? staff.GetValueOrDefault(sid) : null,
        PromotedGameId = i.PromotedGameId,
    };
}
