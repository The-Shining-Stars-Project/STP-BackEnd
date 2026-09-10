namespace CRM.Application.DTOs.Taxonomy;

public class ObjectiveAreaDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    /// <summary>"PartTime" or "Pathways" — which progress framework the area belongs to.</summary>
    public string Track { get; set; } = "PartTime";
    public string? AnnualGoal { get; set; }
    public string? SixMonthBenchmark { get; set; }
    public List<SubSkillDto> SubSkills { get; set; } = new();
}

public class SubSkillDto
{
    public Guid Id { get; set; }
    public Guid ObjectiveAreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int SectionNumber { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public string? ObjectiveAreaName { get; set; }
    public string? ObjectiveAreaColorHex { get; set; }
}

public class SiteDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class StarGroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>
/// The reference dropdown sources mirrored from the spreadsheets' "Do Not Remove-
/// Lists" tabs: objective areas (with nested sub-skills), progress levels, sites, and
/// star groups. Grows further with staff in later phases.
/// </summary>
public class ReferenceListsDto
{
    public List<ObjectiveAreaDto> ObjectiveAreas { get; set; } = new();
    public List<SubSkillDto> SubSkills { get; set; } = new();
    public List<string> ProgressLevels { get; set; } = new();
    public List<SiteDto> Sites { get; set; } = new();
    public List<StarGroupDto> StarGroups { get; set; } = new();
}
