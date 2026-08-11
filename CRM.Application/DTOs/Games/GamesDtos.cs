using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Games;

public class GameSubGoalDto
{
    public Guid SubSkillId { get; set; }
    public string SubSkillName { get; set; } = string.Empty;
    public int SectionNumber { get; set; }
    public string? ObjectiveAreaColorHex { get; set; }
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
}

public class GameSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public GameSource Source { get; set; }
    public GameCategory Category { get; set; }
    public string? CategoryLabel { get; set; }
    /// <summary>Flags enum — serializes as "All" or e.g. "Novice, Intermediate".</summary>
    public GameTier Tiers { get; set; }
    public Guid PrimaryObjectiveAreaId { get; set; }
    public string PrimaryObjectiveAreaName { get; set; } = string.Empty;
    public string PrimaryObjectiveAreaColorHex { get; set; } = string.Empty;
    public string? WhenToUse { get; set; }
    public string? Location { get; set; }
    public Guid? ProgramId { get; set; }
    public string? ProgramName { get; set; }
    public List<GameSubGoalDto> SubGoals { get; set; } = new();
}

public class GameDetailDto : GameSummaryDto
{
    public string? Description { get; set; }
    public string? BestForVariations { get; set; }
}

/// <summary>Server-side filter for the Games Library browser. All fields optional (AND-combined).</summary>
public class GameFilter
{
    /// <summary>Match games that target this tier (bitmask test against the flags).</summary>
    public GameTier? Tier { get; set; }
    public Guid? ObjectiveAreaId { get; set; }
    public Guid? SubSkillId { get; set; }
    public GameCategory? Category { get; set; }
    public string? Query { get; set; }
    /// <summary>Match activities assigned to this program (unassigned activities always match).</summary>
    public Guid? ProgramId { get; set; }
}

public class CreateGameSubGoalDto
{
    public Guid SubSkillId { get; set; }
    public bool IsPrimary { get; set; }
}

public class CreateGameDto
{
    public string Name { get; set; } = string.Empty;
    public GameSource Source { get; set; }
    public GameCategory Category { get; set; }
    public string? CategoryLabel { get; set; }
    public GameTier Tiers { get; set; }
    public Guid PrimaryObjectiveAreaId { get; set; }
    public string? Description { get; set; }
    public string? BestForVariations { get; set; }
    public string? WhenToUse { get; set; }
    public string? Location { get; set; }
    public Guid? ProgramId { get; set; }
    /// <summary>Sub-goals in order; the first is treated as primary. Order sets SortOrder.</summary>
    public List<CreateGameSubGoalDto> SubGoals { get; set; } = new();
}

/// <summary>Full replace — a PUT overwrites all scalar fields and the sub-goal set.</summary>
public class UpdateGameDto : CreateGameDto { }
