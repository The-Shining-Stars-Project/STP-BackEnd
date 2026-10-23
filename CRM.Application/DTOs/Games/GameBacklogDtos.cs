using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Games;

public class GameIdeaDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? StatusNotes { get; set; }
    public string? SourceInspiration { get; set; }
    public GameCategory? TargetCategory { get; set; }
    public bool TeacherSuggested { get; set; }
    public Guid? TeacherSuggestedId { get; set; }
    public string? TeacherSuggestedName { get; set; }
    public Guid? PromotedGameId { get; set; }
}

public class CreateGameIdeaDto
{
    public string Name { get; set; } = string.Empty;
    public string? StatusNotes { get; set; }
    public string? SourceInspiration { get; set; }
    public GameCategory? TargetCategory { get; set; }
    public bool TeacherSuggested { get; set; }
    public Guid? TeacherSuggestedId { get; set; }
}

public class AgeModificationDto
{
    public Guid Id { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string GroupAgeLevel { get; set; } = string.Empty;
    public string Modification { get; set; } = string.Empty;
    public bool TeacherSuggested { get; set; }
    public Guid? TeacherSuggestedId { get; set; }
    public string? TeacherSuggestedName { get; set; }
    public Guid? GameId { get; set; }
}

public class CreateAgeModificationDto
{
    public string GameName { get; set; } = string.Empty;
    public string GroupAgeLevel { get; set; } = string.Empty;
    public string Modification { get; set; } = string.Empty;
    public bool TeacherSuggested { get; set; }
    public Guid? TeacherSuggestedId { get; set; }
    public Guid? GameId { get; set; }
}

/// <summary>Full replace (Oct 2026): the edit form sends every field; a null GameId unlinks.</summary>
public class UpdateAgeModificationDto
{
    public string GameName { get; set; } = string.Empty;
    public string GroupAgeLevel { get; set; } = string.Empty;
    public string Modification { get; set; } = string.Empty;
    public Guid? GameId { get; set; }
}
