using CRM.Application.DTOs.Events;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Application.Services;

/// <inheritdoc cref="IEventAttendanceService"/>
public class EventAttendanceService : IEventAttendanceService
{
    private readonly IUnitOfWork _uow;
    private readonly IProgramAccessService _access;
    private readonly IOrgClock _clock;

    public EventAttendanceService(IUnitOfWork uow, IProgramAccessService access, IOrgClock clock)
    {
        _uow = uow;
        _access = access;
        _clock = clock;
    }

    // ── reads ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<EventSessionSummaryDto>> GetEventsAsync(Guid userId, DateTime from, DateTime to)
    {
        var start = from.Date;
        var end = to.Date.AddDays(1);

        var events = (await _uow.EventSessions.ListAsync(e => e.Date >= start && e.Date < end))
            .OrderByDescending(e => e.Date)
            .ToList();
        if (events.Count == 0) return [];

        var access = await _access.ForUserAsync(userId);
        var scope = await BuildScopeAsync(access);
        var siteNames = await SiteNameMapAsync();

        var ids = events.Select(e => e.Id).ToHashSet();
        var records = await _uow.EventAttendanceRecords.ListAsync(r => ids.Contains(r.EventSessionId));
        var byEvent = records.GroupBy(r => r.EventSessionId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<EventSessionSummaryDto>();
        foreach (var e in events)
        {
            var visible = (byEvent.GetValueOrDefault(e.Id) ?? [])
                .Where(r => scope.Contains(r.ParticipantId))
                .ToList();
            result.Add(await ToSummaryAsync(e, visible, siteNames));
        }
        return result;
    }

    public async Task<EventRosterDto?> GetRosterAsync(Guid userId, Guid eventSessionId)
    {
        var ev = await _uow.EventSessions.GetByIdAsync(eventSessionId);
        if (ev is null) return null;

        var access = await _access.ForUserAsync(userId);
        var scope = await BuildScopeAsync(access);

        var records = await _uow.EventAttendanceRecords.ListAsync(r => r.EventSessionId == eventSessionId);
        // Scope is applied per ENTRY rather than to the register as a whole: the register has
        // no single programme to check, so a teacher sees their own Stars on a mixed list.
        var visible = records.Where(r => scope.Contains(r.ParticipantId)).ToList();

        var participantIds = visible.Select(r => r.ParticipantId).ToHashSet();
        var participants = (await _uow.Participants.ListAsync(p => participantIds.Contains(p.Id)))
            .ToDictionary(p => p.Id);
        var programs = (await _uow.Programs.GetAllAsync()).ToDictionary(p => p.Id);
        var siteNames = await SiteNameMapAsync();

        var entries = visible
            .Select(r =>
            {
                participants.TryGetValue(r.ParticipantId, out var p);
                var prog = p is null ? null : programs.GetValueOrDefault(p.ProgramId);
                return new EventRosterEntryDto
                {
                    RecordId = r.Id,
                    ParticipantId = r.ParticipantId,
                    ParticipantName = p?.FullName ?? "",
                    ParticipantInitials = p?.Initials ?? "",
                    ProgramName = prog?.Name ?? "",
                    ProgramSlug = prog?.Slug ?? "",
                    SiteId = r.SiteId,
                    SiteName = r.SiteId is { } sid ? siteNames.GetValueOrDefault(sid) : null,
                    Status = r.Status,
                    CanMark = ev.Status == SessionStatus.Open,
                };
            })
            .OrderBy(e => e.ParticipantName)
            .ToList();

        return new EventRosterDto
        {
            Event = await ToSummaryAsync(ev, visible, siteNames),
            Entries = entries,
        };
    }

    public async Task<IReadOnlyList<EventCandidateDto>> GetCandidatesAsync(Guid userId, Guid eventSessionId)
    {
        var ev = await _uow.EventSessions.GetByIdAsync(eventSessionId);
        if (ev is null) return [];

        var access = await _access.ForUserAsync(userId);
        var already = (await _uow.EventAttendanceRecords.ListAsync(r => r.EventSessionId == eventSessionId))
            .Select(r => r.ParticipantId).ToHashSet();

        var programs = (await _uow.Programs.GetAllAsync()).ToDictionary(p => p.Id);
        var all = await _uow.Participants.ListAsync(p =>
            p.Status == ParticipantStatus.Active || p.Status == ParticipantStatus.Attention);

        // A dual-enrolled Star matches on either programme but is ONE candidate — selection is
        // by participant, never by programme, which is what keeps the combined list combined.
        return all
            .Where(p => access.IsAdmin
                        || access.CanAccess(p.ProgramId)
                        || (p.SecondaryProgramId is { } sec && access.CanAccess(sec)))
            .OrderBy(p => p.FullName)
            .Select(p =>
            {
                var prog = programs.GetValueOrDefault(p.ProgramId);
                return new EventCandidateDto
                {
                    ParticipantId = p.Id,
                    FullName = p.FullName,
                    Initials = p.Initials,
                    ProgramName = prog?.Name ?? "",
                    ProgramSlug = prog?.Slug ?? "",
                    AlreadyAdded = already.Contains(p.Id),
                };
            })
            .ToList();
    }

    // ── writes ───────────────────────────────────────────────────────────────

    public async Task<EventSessionSummaryDto> CreateAsync(Guid userId, CreateEventSessionDto dto)
    {
        var ev = new EventSession
        {
            Title = dto.Title.Trim(),
            Category = dto.Category,
            Date = ParseDate(dto.Date) ?? _clock.Today,
            Venue = Blank(dto.Venue),
            TimeRange = Blank(dto.TimeRange),
            Status = SessionStatus.Open,
        };
        await _uow.EventSessions.AddAsync(ev);
        await _uow.SaveChangesAsync();

        await _uow.ReplaceEventSessionSitesAsync(ev.Id, dto.SiteIds);
        await _uow.SaveChangesAsync();

        return await ToSummaryAsync(ev, [], await SiteNameMapAsync());
    }

    public async Task<EventSessionSummaryDto?> UpdateAsync(Guid userId, Guid eventSessionId, UpdateEventSessionDto dto)
    {
        var ev = await _uow.EventSessions.GetByIdAsync(eventSessionId);
        if (ev is null) return null;
        RequireOpen(ev);

        ev.Title = dto.Title.Trim();
        ev.Category = dto.Category;
        ev.Date = ParseDate(dto.Date) ?? ev.Date;
        ev.Venue = Blank(dto.Venue);
        ev.TimeRange = Blank(dto.TimeRange);
        ev.HoursLogged = dto.HoursLogged;
        await _uow.EventSessions.UpdateAsync(ev);

        await _uow.ReplaceEventSessionSitesAsync(ev.Id, dto.SiteIds);
        await _uow.SaveChangesAsync();

        var records = await _uow.EventAttendanceRecords.ListAsync(r => r.EventSessionId == ev.Id);
        return await ToSummaryAsync(ev, records, await SiteNameMapAsync());
    }

    public async Task<EventRosterDto?> AddParticipantsAsync(Guid userId, Guid eventSessionId, AddEventParticipantsDto dto)
    {
        var ev = await _uow.EventSessions.GetByIdAsync(eventSessionId);
        if (ev is null) return null;
        RequireOpen(ev);
        await RequireSiteOnEventAsync(ev.Id, dto.SiteId);

        var existing = (await _uow.EventAttendanceRecords.ListAsync(r => r.EventSessionId == eventSessionId))
            .Select(r => r.ParticipantId).ToHashSet();

        // Skip ids already present rather than relying on the unique index to reject them:
        // idempotent by construction, so a double-tapped "add" adds nothing twice.
        var toAdd = dto.ParticipantIds.Distinct().Where(id => !existing.Contains(id)).ToList();
        if (toAdd.Count > 0)
        {
            var real = (await _uow.Participants.ListAsync(p => toAdd.Contains(p.Id))).Select(p => p.Id).ToHashSet();
            foreach (var id in toAdd.Where(real.Contains))
            {
                await _uow.EventAttendanceRecords.AddAsync(new EventAttendanceRecord
                {
                    EventSessionId = eventSessionId,
                    ParticipantId = id,
                    SiteId = dto.SiteId,
                    Status = AttendanceStatus.Unmarked,
                });
            }
            await _uow.SaveChangesAsync();
        }

        return await GetRosterAsync(userId, eventSessionId);
    }

    public async Task<bool> RemoveParticipantAsync(Guid userId, Guid eventSessionId, Guid participantId)
    {
        var ev = await _uow.EventSessions.GetByIdAsync(eventSessionId);
        if (ev is null) return false;
        RequireOpen(ev);

        var record = (await _uow.EventAttendanceRecords.ListAsync(r =>
            r.EventSessionId == eventSessionId && r.ParticipantId == participantId)).FirstOrDefault();
        if (record is null) return false;

        // Removing a marked Star would discard a real observation about whether a child was
        // there. Make it deliberate: unmark first.
        if (record.Status != AttendanceStatus.Unmarked)
            throw new InvalidOperationException(
                "This star has already been marked. Set them back to Unmarked before removing them.");

        await _uow.EventAttendanceRecords.DeleteAsync(record);
        await _uow.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateRecordAsync(Guid userId, Guid recordId, UpdateEventRecordDto dto)
    {
        var record = await _uow.EventAttendanceRecords.GetByIdAsync(recordId);
        if (record is null) return false;

        var ev = await _uow.EventSessions.GetByIdAsync(record.EventSessionId);
        // Deny when the resource cannot be located rather than falling through — same rule as
        // AttendanceService.
        if (ev is null) return false;

        // Per-entry scope: a teacher may mark their own Stars on a mixed register and nobody
        // else's. This is the check that makes a shared register safe.
        var participant = await _uow.Participants.GetByIdAsync(record.ParticipantId);
        if (participant is null) return false;
        RequireParticipantInScope(await _access.ForUserAsync(userId), participant);

        if (ev.Status == SessionStatus.Submitted)
            throw new InvalidOperationException("This event has been submitted and can no longer be edited.");

        await RequireSiteOnEventAsync(ev.Id, dto.SiteId);

        record.Status = dto.Status;
        record.SiteId = dto.SiteId;
        await _uow.EventAttendanceRecords.UpdateAsync(record);
        await _uow.SaveChangesAsync();
        return true;
    }

    public async Task<EventSessionSummaryDto?> SubmitAsync(Guid userId, Guid eventSessionId)
    {
        var ev = await _uow.EventSessions.GetByIdAsync(eventSessionId);
        if (ev is null) return null;
        RequireOpen(ev);

        ev.Status = SessionStatus.Submitted;
        ev.SubmittedAt = DateTime.UtcNow;
        await _uow.EventSessions.UpdateAsync(ev);
        await _uow.SaveChangesAsync();

        var records = await _uow.EventAttendanceRecords.ListAsync(r => r.EventSessionId == ev.Id);
        return await ToSummaryAsync(ev, records, await SiteNameMapAsync());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Participant ids the caller may see: everyone for an admin, else their programmes'.</summary>
    private async Task<HashSet<Guid>> BuildScopeAsync(ProgramAccess access)
    {
        var all = await _uow.Participants.GetAllAsync();
        return all
            .Where(p => access.IsAdmin
                        || access.CanAccess(p.ProgramId)
                        || (p.SecondaryProgramId is { } sec && access.CanAccess(sec)))
            .Select(p => p.Id)
            .ToHashSet();
    }

    private static void RequireParticipantInScope(ProgramAccess access, Participant p)
    {
        if (access.IsAdmin) return;
        if (access.CanAccess(p.ProgramId)) return;
        if (p.SecondaryProgramId is { } sec && access.CanAccess(sec)) return;
        throw new UnauthorizedAccessException("You are not assigned to this star's program.");
    }

    private static void RequireOpen(EventSession ev)
    {
        if (ev.Status == SessionStatus.Submitted)
            throw new InvalidOperationException("This event has been submitted and can no longer be edited.");
    }

    /// <summary>
    /// A record's site must be one of the event's participating locations. Enforced here rather
    /// than by a composite foreign key — this is the only thing keeping the two notions of
    /// "which site" in step, so it must run on every write that sets one.
    /// </summary>
    private async Task RequireSiteOnEventAsync(Guid eventSessionId, Guid? siteId)
    {
        if (siteId is not { } id) return;
        var sites = await _uow.GetEventSessionSitesAsync(eventSessionId);
        if (!sites.Any(s => s.SiteId == id))
            throw new InvalidOperationException("That location is not taking part in this event.");
    }

    private async Task<Dictionary<Guid, string>> SiteNameMapAsync() =>
        (await _uow.Sites.GetAllAsync()).ToDictionary(s => s.Id, s => s.Name);

    private async Task<EventSessionSummaryDto> ToSummaryAsync(
        EventSession ev, IReadOnlyList<EventAttendanceRecord> visibleRecords, Dictionary<Guid, string> siteNames)
    {
        var sites = await _uow.GetEventSessionSitesAsync(ev.Id);
        return new EventSessionSummaryDto
        {
            Id = ev.Id,
            Title = ev.Title,
            Category = ev.Category,
            Date = ev.Date.ToString("yyyy-MM-dd"),
            Venue = ev.Venue,
            TimeRange = ev.TimeRange,
            HoursLogged = ev.HoursLogged,
            Status = ev.Status,
            Sites = sites
                .Select(s => new EventSiteDto { Id = s.SiteId, Name = siteNames.GetValueOrDefault(s.SiteId, "") })
                .OrderBy(s => s.Name)
                .ToList(),
            TotalCount = visibleRecords.Count,
            MarkedCount = visibleRecords.Count(r => r.Status != AttendanceStatus.Unmarked),
            PresentCount = visibleRecords.Count(r => r.Status == AttendanceStatus.Present),
        };
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateTime? ParseDate(string? iso) =>
        DateTime.TryParse(iso, out var d) ? d.Date : null;
}
