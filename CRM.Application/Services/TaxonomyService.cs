using CRM.Application.DTOs.Taxonomy;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Application.Services;

public class TaxonomyService : ITaxonomyService
{
    private readonly IUnitOfWork _uow;

    public TaxonomyService(IUnitOfWork uow) => _uow = uow;

    public async Task<IReadOnlyList<ObjectiveAreaDto>> GetObjectiveAreasAsync()
    {
        var areas = await _uow.ObjectiveAreas.GetAllAsync();
        var subSkills = await _uow.SubSkills.GetAllAsync();
        var byArea = subSkills
            .GroupBy(s => s.ObjectiveAreaId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return areas
            .OrderBy(a => a.SortOrder)
            .Select(a => new ObjectiveAreaDto
            {
                Id = a.Id,
                Name = a.Name,
                Slug = a.Slug,
                ColorHex = a.ColorHex,
                SortOrder = a.SortOrder,
                Track = a.Track.ToString(),
                AnnualGoal = a.AnnualGoal,
                SixMonthBenchmark = a.SixMonthBenchmark,
                SubSkills = byArea.TryGetValue(a.Id, out var list)
                    ? list.OrderBy(s => s.SortOrder).Select(s => ToDto(s, a)).ToList()
                    : new List<SubSkillDto>(),
            })
            .ToList();
    }

    public async Task<IReadOnlyList<SubSkillDto>> GetSubSkillsAsync()
    {
        var areas = await _uow.ObjectiveAreas.GetAllAsync();
        var areaMap = areas.ToDictionary(a => a.Id);
        var subSkills = await _uow.SubSkills.GetAllAsync();

        return subSkills
            .OrderBy(s => s.SectionNumber).ThenBy(s => s.SortOrder)
            .Select(s => ToDto(s, areaMap.GetValueOrDefault(s.ObjectiveAreaId)))
            .ToList();
    }

    public async Task<ReferenceListsDto> GetListsAsync()
    {
        var areas = await GetObjectiveAreasAsync();
        var sites = await _uow.Sites.GetAllAsync();
        var groups = await _uow.StarGroups.GetAllAsync();
        return new ReferenceListsDto
        {
            ObjectiveAreas = areas.ToList(),
            SubSkills = areas.SelectMany(a => a.SubSkills)
                             .OrderBy(s => s.SectionNumber).ThenBy(s => s.SortOrder)
                             .ToList(),
            ProgressLevels = Enum.GetNames<ProgressLevel>().ToList(),
            // Dropdowns get active sites only; Settings lists every site via /api/sites.
            Sites = sites.Where(s => s.IsActive).OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
                         .Select(s => new SiteDto { Id = s.Id, Name = s.Name, Slug = s.Slug, SortOrder = s.SortOrder, IsActive = s.IsActive })
                         .ToList(),
            StarGroups = groups.OrderBy(g => g.SortOrder)
                               .Select(g => new StarGroupDto { Id = g.Id, Name = g.Name, Slug = g.Slug, SortOrder = g.SortOrder })
                               .ToList(),
        };
    }

    private static SubSkillDto ToDto(SubSkill s, ObjectiveArea? area) => new()
    {
        Id = s.Id,
        ObjectiveAreaId = s.ObjectiveAreaId,
        Name = s.Name,
        Slug = s.Slug,
        SectionNumber = s.SectionNumber,
        SortOrder = s.SortOrder,
        IsActive = s.IsActive,
        ObjectiveAreaName = area?.Name,
        ObjectiveAreaColorHex = area?.ColorHex,
    };
}
