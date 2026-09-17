using CRM.Application.DTOs.Files;
using CRM.Application.DTOs.Staff;
using CRM.Application.Files;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CRM.Application.Services;

public class StaffService : IStaffService
{
    public const long MaxFileBytes = FileValidation.DefaultMaxBytes;

    private readonly IUnitOfWork _uow;
    private readonly IOrgClock _clock;
    private readonly IFileStorage _files;
    private readonly ILogger<StaffService> _logger;

    /// <summary>How far ahead a renewal shows up as an alert.</summary>
    public const int TrainingAlertWindowDays = 60;

    public StaffService(IUnitOfWork uow, IOrgClock clock, IFileStorage files, ILogger<StaffService> logger)
    {
        _uow = uow;
        _clock = clock;
        _files = files;
        _logger = logger;
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

        // Renewal alerts: anything dated that comes due within 60 days, or is past due.
        var today = _clock.Today;
        var alertsByStaff = (await _uow.OnboardingItems.GetAllAsync(ct))
            .Where(o => o.ExpiryDate is not null && !o.IsNotApplicable)
            .Select(o => (o.StaffMemberId, Alert: new TrainingAlertDto
            {
                ItemId = o.Id,
                Label = o.Label,
                ExpiryDate = o.ExpiryDate!.Value.ToString("yyyy-MM-dd"),
                DaysUntil = (o.ExpiryDate.Value.Date - today).Days,
            }))
            .Where(x => x.Alert.DaysUntil <= TrainingAlertWindowDays)
            .GroupBy(x => x.StaffMemberId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Alert).OrderBy(a => a.DaysUntil).ToList());

        return staff.Select(s =>
        {
            var progIds = assignmentsByStaff.GetValueOrDefault(s.Id, new());
            var progNames = progIds
                .Select(id => programMap.GetValueOrDefault(id))
                .OfType<string>()
                .ToList();
            var dto = ToSummary(s, progNames);
            if (s.EndDate is null) dto.TrainingAlerts = alertsByStaff.GetValueOrDefault(s.Id) ?? new();
            return dto;
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
            TShirtSize = s.TShirtSize,
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
                RenewalMonths = o.RenewalMonths,
                IsNotApplicable = o.IsNotApplicable,
                HasFile = o.BlobName is not null,
                FileName = o.FileName,
                ContentType = o.ContentType,
                SizeBytes = o.SizeBytes,
                UploadedAt = o.UploadedAt,
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
            TShirtSize = dto.TShirtSize,
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
                RenewalMonths = t.RenewalMonths,
                TemplateItemId = t.Id,
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
        if (dto.StartDate.HasValue) member.StartDate = dto.StartDate.Value.Date;
        if (dto.EndDate.HasValue) member.EndDate = dto.EndDate;
        else if (dto.ClearEndDate) member.EndDate = null;
        if (dto.TShirtSize is not null) member.TShirtSize = dto.TShirtSize;

        await _uow.Staff.UpdateAsync(member);
        await _uow.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    public async Task<StaffDetailDto?> SetOnboardingItemAsync(Guid staffId, Guid itemId, SetOnboardingItemDto dto)
    {
        var member = await _uow.Staff.GetByIdAsync(staffId);
        if (member is null) return null;

        var item = await _uow.OnboardingItems.FirstOrDefaultAsync(o => o.Id == itemId && o.StaffMemberId == staffId);
        if (item is null) return null;

        if (dto.IsNotApplicable.HasValue) item.IsNotApplicable = dto.IsNotApplicable.Value;

        item.IsCompleted = dto.IsCompleted;
        item.CompletedDate = dto.IsCompleted ? (dto.CompletedDate?.Date ?? item.CompletedDate ?? _clock.Today) : null;

        if (dto.ExpiryDate.HasValue) item.ExpiryDate = dto.ExpiryDate;
        else if (dto.ClearExpiry) item.ExpiryDate = null;
        else if (item.RenewalMonths is { } months)
        {
            // A renewable item's expiry follows its completion date: TB done 2026-09-14 with
            // a 48-month interval is due again 2030-09-14. Un-completing it clears the date.
            item.ExpiryDate = item.IsCompleted && item.CompletedDate is { } done ? done.AddMonths(months) : null;
        }
        await _uow.OnboardingItems.UpdateAsync(item);
        // Flush before recounting: ListAsync reads AsNoTracking, so an unsaved
        // toggle would come back with its old IsCompleted value.
        await _uow.SaveChangesAsync();

        await RecomputeProgressAsync(member);
        await _uow.SaveChangesAsync();

        return await GetByIdAsync(staffId);
    }

    /// <summary>Progress % is denormalized on the staff row; N/A items count for neither side.</summary>
    private async Task RecomputeProgressAsync(StaffMember member)
    {
        var items = (await _uow.OnboardingItems.ListAsync(o => o.StaffMemberId == member.Id))
            .Where(o => !o.IsNotApplicable)
            .ToList();
        member.OnboardingProgressPct = ProgressPct(items);
        await _uow.Staff.UpdateAsync(member);
    }

    internal static int ProgressPct(IReadOnlyCollection<OnboardingItem> counted) =>
        counted.Count == 0 ? 0 : (int)Math.Round(counted.Count(o => o.IsCompleted) * 100.0 / counted.Count);

    // ── Onboarding paperwork ──────────────────────────────────────────────────────
    // Same contract as script PDFs: fresh blob per upload, pointer saved before the previous
    // blob is deleted, pointer cleared before the blob on removal.

    public async Task<StaffDetailDto?> AttachOnboardingFileAsync(Guid staffId, Guid itemId, Stream content, string fileName, CancellationToken ct = default)
    {
        var item = await FindItemAsync(staffId, itemId, ct);
        if (item is null) return null;

        var file = FileValidation.Validate(content, fileName, FileValidation.Documents, MaxFileBytes);
        var blobName = $"staff/{staffId:D}/onboarding/{itemId:D}/{Guid.NewGuid():N}{file.Extension}";
        await _files.UploadAsync(blobName, content, file.ContentType, ct);

        var previous = item.BlobName;
        item.BlobName = blobName;
        item.FileName = file.FileName;
        item.ContentType = file.ContentType;
        item.SizeBytes = file.Length;
        item.UploadedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _uow.OnboardingItems.UpdateAsync(item);
            await _uow.SaveChangesAsync();
        }
        catch
        {
            await TryDeleteAsync(blobName);
            throw;
        }

        if (previous is not null && previous != blobName)
            await TryDeleteAsync(previous);

        return await GetByIdAsync(staffId);
    }

    public async Task<StoredFile?> OpenOnboardingFileAsync(Guid staffId, Guid itemId, CancellationToken ct = default)
    {
        var item = await FindItemAsync(staffId, itemId, ct);
        if (item?.BlobName is null) return null;

        var stream = await _files.OpenReadAsync(item.BlobName, ct);
        if (stream is null)
        {
            _logger.LogWarning(
                "Onboarding item {ItemId} points at blob {BlobName}, which no longer exists in storage.",
                itemId, item.BlobName);
            return null;
        }

        return new StoredFile(stream, item.FileName ?? "document", item.ContentType ?? "application/octet-stream", item.SizeBytes);
    }

    public async Task<StaffDetailDto?> RemoveOnboardingFileAsync(Guid staffId, Guid itemId, CancellationToken ct = default)
    {
        var item = await FindItemAsync(staffId, itemId, ct);
        if (item is null) return null;

        var blobName = item.BlobName;
        if (blobName is null) return await GetByIdAsync(staffId);

        item.BlobName = null;
        item.FileName = null;
        item.ContentType = null;
        item.SizeBytes = null;
        item.UploadedAt = null;
        item.UpdatedAt = DateTime.UtcNow;

        await _uow.OnboardingItems.UpdateAsync(item);
        await _uow.SaveChangesAsync();

        await TryDeleteAsync(blobName);
        return await GetByIdAsync(staffId);
    }

    private async Task<OnboardingItem?> FindItemAsync(Guid staffId, Guid itemId, CancellationToken ct)
    {
        if (await _uow.Staff.GetByIdAsync(staffId) is null) return null;
        return await _uow.OnboardingItems.FirstOrDefaultAsync(o => o.Id == itemId && o.StaffMemberId == staffId, ct);
    }

    private async Task TryDeleteAsync(string blobName)
    {
        try
        {
            await _files.DeleteAsync(blobName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete blob {BlobName}; it is now an orphan.", blobName);
        }
    }

    public async Task<IReadOnlyList<ChecklistTemplateItemDto>> GetChecklistTemplateAsync()
    {
        var items = await _uow.ChecklistTemplateItems.GetAllAsync();
        return items
            .OrderBy(t => t.SortOrder)
            .Select(t => new ChecklistTemplateItemDto { Id = t.Id, Section = t.Section, Label = t.Label, RenewalMonths = t.RenewalMonths })
            .ToList();
    }

    public async Task<IReadOnlyList<ChecklistTemplateItemDto>> UpdateChecklistTemplateAsync(UpdateChecklistTemplateDto dto)
    {
        // Upsert by id: a row the editor sends back with its id is the same item (so a
        // relabelled "TB & fingerprinting" stays one item on every checklist rather than
        // becoming a new one beside the old); rows it does not send back are removed.
        var existing = (await _uow.ChecklistTemplateItems.GetAllAsync()).ToDictionary(t => t.Id);
        var seen = new HashSet<Guid>();
        var order = 0;
        var template = new List<ChecklistTemplateItem>();
        foreach (var i in dto.Items.Where(i => !string.IsNullOrWhiteSpace(i.Label)))
        {
            var section = string.IsNullOrWhiteSpace(i.Section) ? "General" : i.Section.Trim();
            var label = i.Label.Trim();
            if (i.Id is { } id && existing.TryGetValue(id, out var row) && seen.Add(id))
            {
                row.Section = section;
                row.Label = label;
                row.SortOrder = order++;
                row.RenewalMonths = i.RenewalMonths;
                await _uow.ChecklistTemplateItems.UpdateAsync(row);
                template.Add(row);
            }
            else
            {
                var t = new ChecklistTemplateItem { Section = section, Label = label, SortOrder = order++, RenewalMonths = i.RenewalMonths };
                template.Add(t);
                await _uow.ChecklistTemplateItems.AddAsync(t);
            }
        }
        foreach (var gone in existing.Values.Where(t => !seen.Contains(t.Id)))
            await _uow.ChecklistTemplateItems.DeleteAsync(gone);

        await SyncChecklistsAsync(template);
        await _uow.SaveChangesAsync();
        return await GetChecklistTemplateAsync();
    }

    /// <summary>
    /// Brings every current staff member's checklist in line with the template ("Edit
    /// checklist doesn't apply to current teachers" — Sep 2026). Items are matched by
    /// section + label, case-insensitively. New template items are added unchecked; items
    /// no longer in the template are removed unless they are completed, marked N/A, or hold
    /// a file — that is history, not a checklist edit. Former staff are left alone.
    /// </summary>
    private async Task SyncChecklistsAsync(IReadOnlyList<ChecklistTemplateItem> template)
    {
        static string Key(string section, string label) => $"{section.Trim()}\u001f{label.Trim()}".ToLowerInvariant();
        var wanted = template.ToDictionary(t => Key(t.Section, t.Label));

        var staff = (await _uow.Staff.GetAllAsync()).Where(s => s.EndDate is null).ToList();
        var itemsByStaff = (await _uow.OnboardingItems.GetAllAsync())
            .GroupBy(o => o.StaffMemberId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var templateIds = template.Select(t => t.Id).ToHashSet();

        foreach (var member in staff)
        {
            var mine = itemsByStaff.GetValueOrDefault(member.Id) ?? new List<OnboardingItem>();
            var byTemplateId = mine.Where(o => o.TemplateItemId is not null).GroupBy(o => o.TemplateItemId!.Value).ToDictionary(g => g.Key, g => g.First());
            var byKey = mine.GroupBy(o => Key(o.Section, o.Label)).ToDictionary(g => g.Key, g => g.First());
            var resulting = new List<OnboardingItem>();
            var matched = new HashSet<Guid>();

            foreach (var t in template)
            {
                // The issued row is found by template id first (a rename still matches),
                // then by section + label for rows issued before ids were recorded.
                var have = byTemplateId.GetValueOrDefault(t.Id) ?? byKey.GetValueOrDefault(Key(t.Section, t.Label));
                if (have is not null && matched.Add(have.Id))
                {
                    have.Section = t.Section;
                    have.Label = t.Label;
                    have.SortOrder = t.SortOrder;
                    have.RenewalMonths = t.RenewalMonths;
                    have.TemplateItemId = t.Id;
                    await _uow.OnboardingItems.UpdateAsync(have);
                    resulting.Add(have);
                }
                else
                {
                    var added = new OnboardingItem
                    {
                        StaffMemberId = member.Id,
                        Section = t.Section,
                        Label = t.Label,
                        SortOrder = t.SortOrder,
                        RenewalMonths = t.RenewalMonths,
                        TemplateItemId = t.Id,
                    };
                    await _uow.OnboardingItems.AddAsync(added);
                    resulting.Add(added);
                }
            }

            foreach (var o in mine.Where(o => !matched.Contains(o.Id)))
            {
                var stillInTemplate = (o.TemplateItemId is { } tid && templateIds.Contains(tid)) || wanted.ContainsKey(Key(o.Section, o.Label));
                if (stillInTemplate || o.IsCompleted || o.IsNotApplicable || o.BlobName is not null) { resulting.Add(o); continue; }
                await _uow.OnboardingItems.DeleteAsync(o);
            }

            member.OnboardingProgressPct = ProgressPct(resulting.Where(o => !o.IsNotApplicable).ToList());
            await _uow.Staff.UpdateAsync(member);
        }
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
            TShirtSize = s.TShirtSize,
            OnboardingProgressPct = s.OnboardingProgressPct,
            ProgramNames = programNames,
        };
}
