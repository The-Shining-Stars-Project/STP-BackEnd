using CRM.Application.DTOs.Roster;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

public class RosterService : IRosterService
{
    private readonly IUnitOfWork _uow;
    private readonly IProgramAccessService _access;

    public RosterService(IUnitOfWork uow, IProgramAccessService access)
    {
        _uow = uow;
        _access = access;
    }

    public async Task<IReadOnlyList<RosterEntryDto>> GetRosterAsync(Guid userId, int year, int quarter, Guid? siteId)
    {
        // The management view is still program-scoped (#1) — "management" describes the
        // columns shown, not a licence to read every program's children.
        var access = await _access.ForUserAsync(userId);
        var participants = await _uow.Participants.GetAllAsync();
        var ctx = await LoadContextAsync(year, quarter);

        var entries = participants
            .Where(p => access.CanAccess(p.ProgramId))
            .Select(p => BuildEntry(p, ctx.AssignmentByParticipant.GetValueOrDefault(p.Id), year, quarter, ctx))
            .Where(e => siteId is null || e.SiteIds.Contains(siteId.Value));

        return Order(entries).ToList();
    }

    public async Task<IReadOnlyList<RosterEntryDto>> GetMyStarsAsync(Guid userId, int year, int quarter)
    {
        var access = await _access.ForUserAsync(userId);
        var participants = await _uow.Participants.GetAllAsync();
        var ctx = await LoadContextAsync(year, quarter);

        var entries = participants
            .Where(p => access.CanAccess(p.ProgramId))
            .Select(p => BuildEntry(p, ctx.AssignmentByParticipant.GetValueOrDefault(p.Id), year, quarter, ctx));

        return Order(entries).ToList();
    }

    public async Task<RosterEntryDto?> UpsertAssignmentAsync(Guid userId, UpsertRosterAssignmentDto dto)
    {
        var participant = await _access.RequireParticipantAsync(userId, dto.ParticipantId);
        if (participant is null) return null;

        var existing = (await _uow.RosterAssignments.ListAsync(
            r => r.ParticipantId == dto.ParticipantId && r.Year == dto.Year && r.Quarter == dto.Quarter)).FirstOrDefault();

        // The single-site form is still accepted; the list wins when both are sent. Only
        // active, real sites survive; order is kept because the first is the primary.
        var requested = dto.SiteIds ?? (dto.SiteId is { } single ? new List<Guid> { single } : new List<Guid>());
        var activeSites = (await _uow.Sites.ListAsync(s => s.IsActive)).Select(s => s.Id).ToHashSet();
        var siteIds = requested.Where(activeSites.Contains).Distinct().ToList();
        var primary = siteIds.Count > 0 ? siteIds[0] : (Guid?)null;

        if (existing is null)
        {
            existing = new RosterAssignment
            {
                ParticipantId = dto.ParticipantId,
                Year = dto.Year,
                Quarter = dto.Quarter,
                SiteId = primary,
                StarGroupId = dto.StarGroupId,
                AssignedStaffId = dto.AssignedStaffId,
                CountedInRatio = dto.CountedInRatio,
                Notes = dto.Notes,
            };
            await _uow.RosterAssignments.AddAsync(existing);
            // The join needs the row's id; the fake and EF both assign it on Add.
            await _uow.SaveChangesAsync();
        }
        else
        {
            existing.SiteId = primary;
            existing.StarGroupId = dto.StarGroupId;
            existing.AssignedStaffId = dto.AssignedStaffId;
            existing.CountedInRatio = dto.CountedInRatio;
            existing.Notes = dto.Notes;
            await _uow.RosterAssignments.UpdateAsync(existing);
        }

        await _uow.ReplaceRosterAssignmentSitesAsync(existing.Id, siteIds);
        await _uow.SaveChangesAsync();

        var ctx = await LoadContextAsync(dto.Year, dto.Quarter);
        return BuildEntry(participant, existing, dto.Year, dto.Quarter, ctx);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private sealed record Ctx(
        Dictionary<Guid, CrmProgram> Programs,
        Dictionary<Guid, Site> Sites,
        Dictionary<Guid, StarGroup> Groups,
        Dictionary<Guid, StaffMember> Staff,
        Dictionary<Guid, RosterAssignment> AssignmentByParticipant,
        Dictionary<Guid, List<Guid>> SitesByAssignment);

    private async Task<Ctx> LoadContextAsync(int year, int quarter)
    {
        var programs = await _uow.Programs.GetAllAsync();
        var sites = await _uow.Sites.GetAllAsync();
        var groups = await _uow.StarGroups.GetAllAsync();
        var staff = await _uow.Staff.GetAllAsync();
        var assignments = await _uow.RosterAssignments.ListAsync(r => r.Year == year && r.Quarter == quarter);
        var links = await _uow.GetRosterAssignmentSitesAsync(assignments.Select(a => a.Id).ToList());
        return new Ctx(
            programs.ToDictionary(p => p.Id),
            sites.ToDictionary(s => s.Id),
            groups.ToDictionary(g => g.Id),
            staff.ToDictionary(s => s.Id),
            assignments.ToDictionary(a => a.ParticipantId),
            links.GroupBy(l => l.RosterAssignmentId).ToDictionary(g => g.Key, g => g.Select(l => l.SiteId).ToList()));
    }

    private static RosterEntryDto BuildEntry(Participant p, RosterAssignment? a, int year, int quarter, Ctx ctx)
    {
        var program = ctx.Programs.GetValueOrDefault(p.ProgramId);
        var dto = new RosterEntryDto
        {
            ParticipantId = p.Id,
            ParticipantName = p.FullName,
            ParticipantInitials = p.Initials,
            Status = p.Status,
            ProgramId = p.ProgramId,
            ProgramName = program?.Name ?? "",
            ProgramSlug = program?.Slug ?? "",
            Quarter = quarter,
            Year = year,
        };

        if (a is not null)
        {
            dto.AssignmentId = a.Id;
            // Rows from before the join existed have only SiteId; the migration backfilled
            // them, but stay tolerant of a bare primary either way.
            var siteIds = ctx.SitesByAssignment.GetValueOrDefault(a.Id) ?? new List<Guid>();
            if (a.SiteId is { } legacy && !siteIds.Contains(legacy)) siteIds.Insert(0, legacy);
            else if (a.SiteId is { } prim && siteIds.Count > 0 && siteIds[0] != prim) { siteIds.Remove(prim); siteIds.Insert(0, prim); }
            dto.SiteIds = siteIds;
            dto.SiteNames = siteIds.Select(id => ctx.Sites.GetValueOrDefault(id)?.Name).OfType<string>().ToList();
            dto.SiteId = siteIds.Count > 0 ? siteIds[0] : null;
            dto.SiteName = dto.SiteNames.Count > 0 ? dto.SiteNames[0] : null;
            dto.StarGroupId = a.StarGroupId;
            dto.StarGroupName = a.StarGroupId is { } gid ? ctx.Groups.GetValueOrDefault(gid)?.Name : null;
            dto.AssignedStaffId = a.AssignedStaffId;
            dto.AssignedStaffName = a.AssignedStaffId is { } stid ? ctx.Staff.GetValueOrDefault(stid)?.FullName : null;
            dto.CountedInRatio = a.CountedInRatio;
            dto.Notes = a.Notes;
        }
        return dto;
    }

    private static IEnumerable<RosterEntryDto> Order(IEnumerable<RosterEntryDto> entries) =>
        entries.OrderBy(e => e.SiteName ?? "~")   // unassigned (null) sorts last
               .ThenBy(e => e.StarGroupName ?? "~")
               .ThenBy(e => e.ParticipantName);
}
