using CRM.Application.DTOs.Staff;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

public class StaffService : IStaffService
{
    private readonly IUnitOfWork _uow;
    private readonly IOrgClock _clock;

    public StaffService(IUnitOfWork uow, IOrgClock clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<IReadOnlyList<StaffSummaryDto>> GetAllAsync(CancellationToken ct = default)
    {
        var staff = await _uow.Staff.GetAllAsync(ct);
        var programs = await _uow.Programs.GetAllAsync(ct);
        var assignments = await _uow.GetStaffProgramAssignmentsAsync();

        var programMap = programs.ToDictionary(p => p.Id, p => p.Name);
        var assignmentsByStaff = assignments
            .GroupBy(a => a.StaffMemberId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.ProgramId).ToList());

        return staff.Select(s =>
        {
            var progIds = assignmentsByStaff.GetValueOrDefault(s.Id, new());
            var progNames = progIds
                .Select(id => programMap.GetValueOrDefault(id))
                .OfType<string>()
                .ToList();
            return ToSummary(s, progNames);
        }).ToList();
    }

    public async Task<StaffDetailDto?> GetByIdAsync(Guid id)
    {
        var s = await _uow.Staff.GetByIdAsync(id);
        if (s is null) return null;

        var programs = await _uow.Programs.GetAllAsync();
        var assignments = await _uow.GetStaffProgramAssignmentsAsync();
        var onboardingItems = (await _uow.OnboardingItems.ListAsync(o => o.StaffMemberId == id))
            .OrderBy(o => o.SortOrder).ThenBy(o => o.CreatedAt).ToList();

        var programMap = programs.ToDictionary(p => p.Id, p => p.Name);
        var progNames = assignments
            .Where(a => a.StaffMemberId == id)
            .Select(a => programMap.GetValueOrDefault(a.ProgramId))
            .OfType<string>()
            .ToList();

        return new StaffDetailDto
        {
            Id = s.Id,
            FullName = s.FullName,
            Initials = s.Initials,
            Role = s.Role,
            StartDate = s.StartDate.ToString("yyyy-MM-dd"),
            EndDate = s.EndDate?.ToString("yyyy-MM-dd"),
            IsFormer = s.EndDate is not null,
            OnboardingProgressPct = s.OnboardingProgressPct,
            ProgramNames = progNames,
            OnboardingItems = onboardingItems.Select(o => new OnboardingItemDto
            {
                Id = o.Id,
                Section = o.Section,
                Label = o.Label,
                IsCompleted = o.IsCompleted,
                CompletedDate = o.CompletedDate?.ToString("yyyy-MM-dd"),
                ExpiryDate = o.ExpiryDate?.ToString("yyyy-MM-dd"),
            }).ToList(),
        };
    }

    public async Task<StaffDetailDto> CreateAsync(CreateStaffDto dto)
    {
        var member = new StaffMember
        {
            FullName = dto.FullName,
            Initials = dto.Initials,
            Role = dto.Role,
            StartDate = dto.StartDate ?? _clock.Today,
        };

        await _uow.Staff.AddAsync(member);

        // Issue the current checklist template to the new hire. Their copy is
        // independent — later template edits don't rewrite existing checklists.
        var template = await _uow.ChecklistTemplateItems.GetAllAsync();
        foreach (var t in template.OrderBy(t => t.SortOrder))
            await _uow.OnboardingItems.AddAsync(new OnboardingItem
            {
                StaffMemberId = member.Id,
                Section = t.Section,
                Label = t.Label,
                SortOrder = t.SortOrder,
            });

        await _uow.SaveChangesAsync();

        if (dto.ProgramIds is { Count: > 0 })
        {
            foreach (var progId in dto.ProgramIds)
                await _uow.AddStaffProgramAssignmentAsync(new StaffProgramAssignment
                {
                    StaffMemberId = member.Id,
                    ProgramId = progId,
                });
            await _uow.SaveChangesAsync();
        }

        return (await GetByIdAsync(member.Id))!;
    }

    public async Task<StaffDetailDto?> UpdateAsync(Guid id, UpdateStaffDto dto)
    {
        var member = await _uow.Staff.GetByIdAsync(id);
        if (member is null) return null;

        if (dto.FullName is not null) member.FullName = dto.FullName;
        if (dto.Initials is not null) member.Initials = dto.Initials;
        if (dto.Role.HasValue) member.Role = dto.Role.Value;
        if (dto.EndDate.HasValue) member.EndDate = dto.EndDate;
        else if (dto.ClearEndDate) member.EndDate = null;

        await _uow.Staff.UpdateAsync(member);
        await _uow.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    public async Task<StaffDetailDto?> SetOnboardingItemAsync(Guid staffId, Guid itemId, bool isCompleted)
    {
        var member = await _uow.Staff.GetByIdAsync(staffId);
        if (member is null) return null;

        var item = await _uow.OnboardingItems.FirstOrDefaultAsync(o => o.Id == itemId && o.StaffMemberId == staffId);
        if (item is null) return null;

        item.IsCompleted = isCompleted;
        item.CompletedDate = isCompleted ? _clock.Today : null;
        await _uow.OnboardingItems.UpdateAsync(item);
        // Flush before recounting: ListAsync reads AsNoTracking, so an unsaved
        // toggle would come back with its old IsCompleted value.
        await _uow.SaveChangesAsync();

        // Progress % is denormalized on the staff row; keep it in sync.
        var items = await _uow.OnboardingItems.ListAsync(o => o.StaffMemberId == staffId);
        member.OnboardingProgressPct = items.Count == 0
            ? 0
            : (int)Math.Round(items.Count(o => o.IsCompleted) * 100.0 / items.Count);
        await _uow.Staff.UpdateAsync(member);
        await _uow.SaveChangesAsync();

        return await GetByIdAsync(staffId);
    }

    public async Task<IReadOnlyList<ChecklistTemplateItemDto>> GetChecklistTemplateAsync()
    {
        var items = await _uow.ChecklistTemplateItems.GetAllAsync();
        return items
            .OrderBy(t => t.SortOrder)
            .Select(t => new ChecklistTemplateItemDto { Section = t.Section, Label = t.Label })
            .ToList();
    }

    public async Task<IReadOnlyList<ChecklistTemplateItemDto>> UpdateChecklistTemplateAsync(UpdateChecklistTemplateDto dto)
    {
        // Replace-all: the template is a small ordered list, not per-row edited.
        var existing = await _uow.ChecklistTemplateItems.GetAllAsync();
        foreach (var t in existing)
            await _uow.ChecklistTemplateItems.DeleteAsync(t);

        var order = 0;
        foreach (var i in dto.Items.Where(i => !string.IsNullOrWhiteSpace(i.Label)))
            await _uow.ChecklistTemplateItems.AddAsync(new ChecklistTemplateItem
            {
                Section = string.IsNullOrWhiteSpace(i.Section) ? "General" : i.Section.Trim(),
                Label = i.Label.Trim(),
                SortOrder = order++,
            });

        await _uow.SaveChangesAsync();
        return await GetChecklistTemplateAsync();
    }

    private static StaffSummaryDto ToSummary(StaffMember s, List<string> programNames) =>
        new()
        {
            Id = s.Id,
            FullName = s.FullName,
            Initials = s.Initials,
            Role = s.Role,
            StartDate = s.StartDate.ToString("yyyy-MM-dd"),
            EndDate = s.EndDate?.ToString("yyyy-MM-dd"),
            IsFormer = s.EndDate is not null,
            OnboardingProgressPct = s.OnboardingProgressPct,
            ProgramNames = programNames,
        };
}
