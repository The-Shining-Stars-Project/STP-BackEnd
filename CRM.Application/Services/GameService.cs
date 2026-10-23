using CRM.Application.DTOs.Games;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

public class GameService : IGameService
{
    private readonly IUnitOfWork _uow;

    public GameService(IUnitOfWork uow) => _uow = uow;

    public async Task<IReadOnlyList<GameSummaryDto>> QueryAsync(GameFilter filter)
    {
        var games = await _uow.Games.GetAllAsync();
        var ctx = await LoadContextAsync();

        IEnumerable<Game> q = games;

        if (filter.Tier is { } tier && tier != Domain.Enums.GameTier.None)
            q = q.Where(g => (g.Tiers & tier) != 0);

        if (filter.ObjectiveAreaId is { } areaId)
            q = q.Where(g => g.PrimaryObjectiveAreaId == areaId);

        if (filter.Category is { } cat)
            q = q.Where(g => g.Category == cat);

        if (filter.ProgramId is { } progId)
            q = q.Where(g => g.ProgramId is null || g.ProgramId == progId);

        if (filter.SubSkillId is { } subId)
            q = q.Where(g => ctx.SubGoalsByGame.TryGetValue(g.Id, out var sgs)
                             && sgs.Any(sg => sg.SubSkillId == subId));

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = filter.Query.Trim();
            q = q.Where(g =>
                g.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (g.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return q
            .OrderBy(g => g.Category)
            .ThenBy(g => g.Name)
            .Select(g => ToSummary(g, ctx))
            .ToList();
    }

    public async Task<GameDetailDto?> GetByIdAsync(Guid id)
    {
        var game = await _uow.Games.GetByIdAsync(id);
        if (game is null) return null;
        var dto = ToDetail(game, await LoadContextAsync());

        // Linked modifications ride along so the drawer needs one call (Oct 2026).
        var mods = await _uow.AgeModifications.ListAsync(m => m.GameId == id);
        if (mods.Count > 0)
        {
            var staff = (await _uow.Staff.GetAllAsync()).ToDictionary(s => s.Id, s => s.FullName);
            dto.Modifications = mods
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new AgeModificationDto
                {
                    Id = m.Id,
                    GameName = m.GameName,
                    GroupAgeLevel = m.GroupAgeLevel,
                    Modification = m.Modification,
                    TeacherSuggested = m.TeacherSuggested,
                    TeacherSuggestedId = m.TeacherSuggestedId,
                    TeacherSuggestedName = m.TeacherSuggestedId is { } sid ? staff.GetValueOrDefault(sid) : null,
                    GameId = m.GameId,
                })
                .ToList();
        }
        return dto;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var game = await _uow.Games.GetByIdAsync(id);
        if (game is null) return false;

        // DataSeeder.SeedGamesLibraryAsync runs in every environment and its only guard is
        // "any game exists" — an empty table reloads the whole default library on restart.
        if ((await _uow.Games.GetAllAsync()).Count <= 1)
            throw new InvalidOperationException("The library can't be emptied — add the replacement activity before deleting this one.");

        foreach (var sg in await _uow.GameSubGoals.ListAsync(sg => sg.GameId == id))
            await _uow.GameSubGoals.DeleteAsync(sg);

        // Keep the running log of modifications; they just lose the link.
        foreach (var mod in await _uow.AgeModifications.ListAsync(m => m.GameId == id))
        {
            mod.GameId = null;
            await _uow.AgeModifications.UpdateAsync(mod);
        }

        // The idea this game was promoted from goes back to To Develop.
        foreach (var idea in await _uow.GameIdeas.ListAsync(i => i.PromotedGameId == id))
        {
            idea.PromotedGameId = null;
            await _uow.GameIdeas.UpdateAsync(idea);
        }

        await _uow.Games.DeleteAsync(game);
        await _uow.SaveChangesAsync();
        return true;
    }

    public async Task<GameDetailDto> CreateAsync(CreateGameDto dto)
    {
        var game = new Game
        {
            Name = dto.Name,
            Source = dto.Source,
            Category = dto.Category,
            CategoryLabel = dto.CategoryLabel,
            Tiers = dto.Tiers,
            PrimaryObjectiveAreaId = dto.PrimaryObjectiveAreaId,
            Description = dto.Description,
            BestForVariations = dto.BestForVariations,
            WhenToUse = dto.WhenToUse,
            Location = dto.Location,
            ProgramId = dto.ProgramId,
        };
        await _uow.Games.AddAsync(game);

        var order = 0;
        foreach (var sg in dto.SubGoals)
            await _uow.GameSubGoals.AddAsync(new GameSubGoal
            {
                GameId = game.Id,
                SubSkillId = sg.SubSkillId,
                IsPrimary = sg.IsPrimary,
                SortOrder = order++,
            });

        await _uow.SaveChangesAsync();
        return ToDetail(game, await LoadContextAsync());
    }

    public async Task<GameDetailDto?> UpdateAsync(Guid id, UpdateGameDto dto)
    {
        var game = await _uow.Games.GetByIdAsync(id);
        if (game is null) return null;

        game.Name = dto.Name;
        game.Source = dto.Source;
        game.Category = dto.Category;
        game.CategoryLabel = dto.CategoryLabel;
        game.Tiers = dto.Tiers;
        game.PrimaryObjectiveAreaId = dto.PrimaryObjectiveAreaId;
        game.Description = dto.Description;
        game.BestForVariations = dto.BestForVariations;
        game.WhenToUse = dto.WhenToUse;
        game.Location = dto.Location;
        game.ProgramId = dto.ProgramId;
        await _uow.Games.UpdateAsync(game);

        // Replace the sub-goal set.
        var existing = await _uow.GameSubGoals.ListAsync(sg => sg.GameId == id);
        foreach (var old in existing)
            await _uow.GameSubGoals.DeleteAsync(old);

        var order = 0;
        foreach (var sg in dto.SubGoals)
            await _uow.GameSubGoals.AddAsync(new GameSubGoal
            {
                GameId = id,
                SubSkillId = sg.SubSkillId,
                IsPrimary = sg.IsPrimary,
                SortOrder = order++,
            });

        await _uow.SaveChangesAsync();
        return ToDetail(game, await LoadContextAsync());
    }

    // ── Mapping helpers ─────────────────────────────────────────────────────────

    private sealed record Ctx(
        Dictionary<Guid, ObjectiveArea> AreaById,
        Dictionary<Guid, SubSkill> SubSkillById,
        Dictionary<Guid, List<GameSubGoal>> SubGoalsByGame,
        Dictionary<Guid, string> ProgramNameById);

    private async Task<Ctx> LoadContextAsync()
    {
        var areas = await _uow.ObjectiveAreas.GetAllAsync();
        var subSkills = await _uow.SubSkills.GetAllAsync();
        var subGoals = await _uow.GameSubGoals.GetAllAsync();
        var programs = await _uow.Programs.GetAllAsync();
        return new Ctx(
            areas.ToDictionary(a => a.Id),
            subSkills.ToDictionary(s => s.Id),
            subGoals.GroupBy(sg => sg.GameId).ToDictionary(g => g.Key, g => g.ToList()),
            programs.ToDictionary(p => p.Id, p => p.Name));
    }

    private static List<GameSubGoalDto> SubGoalDtos(Guid gameId, Ctx ctx)
    {
        if (!ctx.SubGoalsByGame.TryGetValue(gameId, out var list)) return new();
        return list
            .OrderBy(sg => sg.SortOrder)
            .Select(sg =>
            {
                ctx.SubSkillById.TryGetValue(sg.SubSkillId, out var skill);
                ObjectiveArea? area = skill is not null ? ctx.AreaById.GetValueOrDefault(skill.ObjectiveAreaId) : null;
                return new GameSubGoalDto
                {
                    SubSkillId = sg.SubSkillId,
                    SubSkillName = skill?.Name ?? "",
                    SectionNumber = skill?.SectionNumber ?? 0,
                    ObjectiveAreaColorHex = area?.ColorHex,
                    IsPrimary = sg.IsPrimary,
                    SortOrder = sg.SortOrder,
                };
            })
            .ToList();
    }

    private static void FillSummary(GameSummaryDto dto, Game g, Ctx ctx)
    {
        var area = ctx.AreaById.GetValueOrDefault(g.PrimaryObjectiveAreaId);
        dto.Id = g.Id;
        dto.Name = g.Name;
        dto.Source = g.Source;
        dto.Category = g.Category;
        dto.CategoryLabel = g.CategoryLabel;
        dto.Tiers = g.Tiers;
        dto.PrimaryObjectiveAreaId = g.PrimaryObjectiveAreaId;
        dto.PrimaryObjectiveAreaName = area?.Name ?? "";
        dto.PrimaryObjectiveAreaColorHex = area?.ColorHex ?? "";
        dto.WhenToUse = g.WhenToUse;
        dto.Location = g.Location;
        dto.ProgramId = g.ProgramId;
        dto.ProgramName = g.ProgramId is { } pid ? ctx.ProgramNameById.GetValueOrDefault(pid) : null;
        dto.SubGoals = SubGoalDtos(g.Id, ctx);
    }

    private static GameSummaryDto ToSummary(Game g, Ctx ctx)
    {
        var dto = new GameSummaryDto();
        FillSummary(dto, g, ctx);
        return dto;
    }

    private static GameDetailDto ToDetail(Game g, Ctx ctx)
    {
        var dto = new GameDetailDto { Description = g.Description, BestForVariations = g.BestForVariations };
        FillSummary(dto, g, ctx);
        return dto;
    }
}
