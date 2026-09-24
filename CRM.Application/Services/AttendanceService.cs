using CRM.Application.DTOs.Attendance;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Application.Services;

public class AttendanceService : IAttendanceService
{
    private readonly IUnitOfWork _uow;
    private readonly IProgramAccessService _access;
    private readonly IOrgClock _clock;

    public AttendanceService(IUnitOfWork uow, IProgramAccessService access, IOrgClock clock)
    {
        _uow = uow;
        _access = access;
        _clock = clock;
    }

    public async Task<AttendanceSessionDto?> GetSessionAsync(Guid userId, Guid sessionId)
    {
        var session = await _uow.Sessions.GetByIdAsync(sessionId);
        if (session is null) return null;

        (await _access.ForUserAsync(userId)).Require(session.ProgramId);

        var records = await _uow.Attendance.ListAsync(r => r.SessionId == sessionId);

        var participantIds = records.Select(r => r.ParticipantId).ToHashSet();
        var participants = await _uow.Participants.ListAsync(p => participantIds.Contains(p.Id));
        var participantMap = participants.ToDictionary(p => p.Id);

        return new AttendanceSessionDto
        {
            SessionId = session.Id,
            ProgramId = session.ProgramId,
            Date = session.Date.ToString("yyyy-MM-dd"),
            Room = session.Room,
            TimeRange = session.TimeRange,
            Records = records.Select(r =>
            {
                var participant = participantMap.GetValueOrDefault(r.ParticipantId);
                return new AttendanceRecordDto
                {
                    Id = r.Id,
                    ParticipantId = r.ParticipantId,
                    ParticipantName = participant?.FullName ?? string.Empty,
                    ParticipantInitials = participant?.Initials ?? string.Empty,
                    Status = r.Status,
                    Group = r.Group,
                    Notes = new(),
                };
            }).ToList(),
        };
    }

    public async Task<bool> UpdateRecordAsync(Guid userId, Guid recordId, UpdateAttendanceDto dto)
    {
        var record = await _uow.Attendance.GetByIdAsync(recordId);
        if (record is null) return false;

        // Only teachers assigned to the record's program (or an Admin) may edit it. A record
        // whose session can't be resolved is treated as not-found rather than skipping the
        // check — deny when the resource can't be located, never fall through.
        var session = await _uow.Sessions.GetByIdAsync(record.SessionId);
        if (session is null) return false;

        (await _access.ForUserAsync(userId)).Require(session.ProgramId);

        // A submitted session is finalized — its records are locked.
        if (session.Status == SessionStatus.Submitted)
            throw new InvalidOperationException("This session has been submitted and can no longer be edited.");

        record.Status = dto.Status;
        await _uow.Attendance.UpdateAsync(record);
        await _uow.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyList<ScheduledSessionDto>> GetScheduledForUserAsync(Guid userId, DateTime date)
    {
        var day = date.Date;
        var nextDay = day.AddDays(1);
        var todayFlag = ToFlag(day.DayOfWeek);

        var access = await _access.ForUserAsync(userId);
        var programs = (await _uow.Programs.GetAllAsync()).Where(p => access.CanAccess(p.Id)).ToList();
        if (programs.Count == 0) return new List<ScheduledSessionDto>();
        var allowed = programs.Select(p => p.Id).ToHashSet();

        // Attendance is tracked for Active, Needs-Attention, and Auth-Pending stars (client
        // rule — auth-pending stars already attend while their authorization is processed).
        // A star counts toward every program they're enrolled in — primary or secondary
        // (PT+Pathways).
        var attendanceEligible = (await _uow.Participants.ListAsync(
                p => p.Status == ParticipantStatus.Active
                     || p.Status == ParticipantStatus.Attention
                     || p.Status == ParticipantStatus.AuthPending)).ToList();
        var activeByProgram = allowed.ToDictionary(
            id => id,
            id => attendanceEligible.Count(p => p.ProgramId == id || p.SecondaryProgramId == id));

        var sessionByProgram = (await _uow.Sessions.ListAsync(s => s.Date >= day && s.Date < nextDay))
            .Where(s => allowed.Contains(s.ProgramId))
            .GroupBy(s => s.ProgramId)
            .ToDictionary(g => g.Key, g => g.First());

        var sessionIds = sessionByProgram.Values.Select(s => s.Id).ToHashSet();
        var markedBySession = (sessionIds.Count == 0
                ? new List<AttendanceRecord>()
                : (await _uow.Attendance.ListAsync(r => sessionIds.Contains(r.SessionId))).ToList())
            .GroupBy(r => r.SessionId)
            .ToDictionary(g => g.Key, g => g.Count(r => r.Status != AttendanceStatus.Unmarked));

        var cards = new List<ScheduledSessionDto>();
        foreach (var program in programs.Where(p => allowed.Contains(p.Id)))
        {
            var meetsToday = todayFlag != MeetingDays.None && program.MeetingDays.HasFlag(todayFlag);
            sessionByProgram.TryGetValue(program.Id, out var session);

            // Show a program only if it's scheduled today, or already has a session started today.
            if (!meetsToday && session is null) continue;

            var marked = session is not null ? markedBySession.GetValueOrDefault(session.Id, 0) : 0;
            cards.Add(new ScheduledSessionDto
            {
                SessionId = session?.Id,
                ProgramId = program.Id,
                ProgramSlug = program.Slug,
                ProgramName = program.Name,
                ColorHex = program.ColorHex,
                Date = day.ToString("yyyy-MM-dd"),
                TimeRange = session?.TimeRange ?? FormatTimeRange(program.StartTime, program.EndTime),
                Room = session?.Room ?? program.DefaultLocation,
                // "in-progress" only once at least one record is marked — an empty session
                // (or no session yet) reads as "not-started".
                Status = session?.Status == SessionStatus.Submitted
                    ? "submitted"
                    : marked > 0 ? "in-progress" : "not-started",
                MarkedCount = marked,
                TotalCount = activeByProgram.GetValueOrDefault(program.Id, 0),
                IsAdHoc = session is not null && !meetsToday,
            });
        }

        return cards.OrderBy(c => c.ProgramName).ToList();
    }

    public async Task<SessionRosterDto?> GetOrCreateSessionAsync(Guid userId, Guid programId, DateTime date)
    {
        var day = date.Date;
        var nextDay = day.AddDays(1);

        var program = await _uow.Programs.GetByIdAsync(programId);
        if (program is null) return null;

        (await _access.ForUserAsync(userId)).Require(programId);

        var session = (await _uow.Sessions.ListAsync(s => s.ProgramId == programId && s.Date >= day && s.Date < nextDay))
            .FirstOrDefault();
        if (session is null)
        {
            session = new Session
            {
                ProgramId = programId,
                Date = day,
                Room = program.DefaultLocation,
                TimeRange = FormatTimeRange(program.StartTime, program.EndTime),
                Status = SessionStatus.Open,
            };
            await _uow.Sessions.AddAsync(session);
            await _uow.SaveChangesAsync();
        }

        var participants = await _uow.Participants.ListAsync(
            p => (p.ProgramId == programId || p.SecondaryProgramId == programId)
                 && (p.Status == ParticipantStatus.Active
                     || p.Status == ParticipantStatus.Attention
                     || p.Status == ParticipantStatus.AuthPending));

        var recordByParticipant = (await _uow.Attendance.ListAsync(r => r.SessionId == session.Id))
            .GroupBy(r => r.ParticipantId)
            .ToDictionary(g => g.Key, g => g.First());

        var created = false;
        foreach (var p in participants)
        {
            if (recordByParticipant.ContainsKey(p.Id)) continue;
            var rec = new AttendanceRecord
            {
                ParticipantId = p.Id,
                SessionId = session.Id,
                Status = AttendanceStatus.Unmarked,
            };
            await _uow.Attendance.AddAsync(rec);
            recordByParticipant[p.Id] = rec;
            created = true;
        }
        if (created) await _uow.SaveChangesAsync();

        return await BuildSessionRosterAsync(program, session, participants, recordByParticipant);
    }

    public async Task<SessionRosterDto?> GetProgramSessionReadOnlyAsync(Guid userId, Guid programId, DateTime date)
    {
        var day = date.Date;
        var nextDay = day.AddDays(1);

        var program = await _uow.Programs.GetByIdAsync(programId);
        if (program is null) return null;

        (await _access.ForUserAsync(userId)).Require(programId);

        // Read-only (#23): a GET must never create sessions or records — prefetches and
        // crawlers were able to open sessions for programs that never met.
        var session = (await _uow.Sessions.ListAsync(s => s.ProgramId == programId && s.Date >= day && s.Date < nextDay))
            .FirstOrDefault();
        if (session is null) return null;

        var participants = await _uow.Participants.ListAsync(
            p => (p.ProgramId == programId || p.SecondaryProgramId == programId)
                 && (p.Status == ParticipantStatus.Active
                     || p.Status == ParticipantStatus.Attention
                     || p.Status == ParticipantStatus.AuthPending));

        var recordByParticipant = (await _uow.Attendance.ListAsync(r => r.SessionId == session.Id))
            .GroupBy(r => r.ParticipantId)
            .ToDictionary(g => g.Key, g => g.First());

        return await BuildSessionRosterAsync(program, session, participants, recordByParticipant);
    }

    /// <summary>Assembles the roster DTO from already-loaded session/participants/records. Loads notes.</summary>
    private async Task<SessionRosterDto> BuildSessionRosterAsync(
        CrmProgram program,
        Session session,
        IReadOnlyList<Participant> participants,
        Dictionary<Guid, AttendanceRecord> recordByParticipant)
    {
        var recordIds = recordByParticipant.Values.Select(r => r.Id).ToHashSet();
        var notesByRecord = (recordIds.Count == 0
                ? new List<AttendanceNote>()
                : (await _uow.AttendanceNotes.ListAsync(n => recordIds.Contains(n.AttendanceRecordId))).ToList())
            .GroupBy(n => n.AttendanceRecordId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var entries = participants
            .Where(p => recordByParticipant.ContainsKey(p.Id))
            .Select(p =>
            {
                var rec = recordByParticipant[p.Id];
                return new AttendanceRosterEntryDto
                {
                    RecordId = rec.Id,
                    ParticipantId = p.Id,
                    FullName = p.FullName,
                    Initials = p.Initials,
                    ProgramId = program.Id,
                    ProgramSlug = program.Slug,
                    ProgramName = program.Name,
                    Status = rec.Status,
                    Notes = notesByRecord.GetValueOrDefault(rec.Id, new())
                        .Select(n => new AttendanceNoteDto { Id = n.Id, Content = n.Content, NoteType = n.NoteType })
                        .ToList(),
                };
            })
            .OrderBy(e => e.FullName)
            .ToList();

        return new SessionRosterDto
        {
            SessionId = session.Id,
            ProgramId = program.Id,
            ProgramSlug = program.Slug,
            ProgramTrack = program.Track,
            ProgramName = program.Name,
            ColorHex = program.ColorHex,
            Date = session.Date.ToString("yyyy-MM-dd"),
            TimeRange = session.TimeRange,
            Room = session.Room,
            Status = session.Status == SessionStatus.Submitted ? "submitted" : "open",
            SubmittedAt = session.SubmittedAt,
            HoursLogged = session.HoursLogged,
            Entries = entries,
        };
    }

    public async Task<bool> SetSessionHoursAsync(Guid userId, Guid sessionId, decimal? hours)
    {
        var session = await _uow.Sessions.GetByIdAsync(sessionId);
        if (session is null) return false;

        (await _access.ForUserAsync(userId)).Require(session.ProgramId);

        session.HoursLogged = hours;
        await _uow.Sessions.UpdateAsync(session);
        await _uow.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SubmitSessionAsync(Guid userId, Guid sessionId)
    {
        var session = await _uow.Sessions.GetByIdAsync(sessionId);
        if (session is null) return false;

        (await _access.ForUserAsync(userId)).Require(session.ProgramId);

        session.Status = SessionStatus.Submitted;
        session.SubmittedAt = _clock.UtcNow;   // an instant, so UTC is correct
        await _uow.Sessions.UpdateAsync(session);
        await _uow.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReopenSessionAsync(Guid userId, Guid sessionId)
    {
        var session = await _uow.Sessions.GetByIdAsync(sessionId);
        if (session is null) return false;

        (await _access.ForUserAsync(userId)).Require(session.ProgramId);

        // Idempotent: reopening an open session is a no-op rather than an error.
        if (session.Status == SessionStatus.Open) return true;
        session.Status = SessionStatus.Open;
        session.SubmittedAt = null;
        await _uow.Sessions.UpdateAsync(session);
        await _uow.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyList<AttendanceRosterEntryDto>> GetTodayRosterReadOnlyAsync(
        Guid userId, CancellationToken ct = default)
    {
        // The organisation's today, not UTC's (#7) — an afternoon session in California is
        // already "tomorrow" in UTC for part of the year.
        var today = _clock.Today;
        var tomorrow = today.AddDays(1);

        // Scoped to the caller's programs (#1). Filtering the sessions is enough — every
        // record hangs off one, so out-of-scope records never enter the result.
        var access = await _access.ForUserAsync(userId);
        var sessionIds = (await _uow.Sessions.ListAsync(s => s.Date >= today && s.Date < tomorrow, ct))
            .Where(s => access.CanAccess(s.ProgramId))
            .Select(s => s.Id)
            .ToHashSet();
        if (sessionIds.Count == 0) return new List<AttendanceRosterEntryDto>();

        var records = await _uow.Attendance.ListAsync(r => sessionIds.Contains(r.SessionId));
        if (records.Count == 0) return new List<AttendanceRosterEntryDto>();

        var participantIds = records.Select(r => r.ParticipantId).ToHashSet();
        var participants = (await _uow.Participants.ListAsync(p => participantIds.Contains(p.Id)))
            .ToDictionary(p => p.Id);
        var programs = (await _uow.Programs.GetAllAsync()).ToDictionary(p => p.Id);

        return records
            .Where(r => participants.ContainsKey(r.ParticipantId))
            .Select(r =>
            {
                var p = participants[r.ParticipantId];
                var prog = programs.GetValueOrDefault(p.ProgramId);
                return new AttendanceRosterEntryDto
                {
                    RecordId = r.Id,
                    ParticipantId = p.Id,
                    FullName = p.FullName,
                    Initials = p.Initials,
                    ProgramId = p.ProgramId,
                    ProgramSlug = prog?.Slug ?? string.Empty,
                    ProgramName = prog?.Name ?? string.Empty,
                    Status = r.Status,
                    Notes = new(),
                };
            })
            .OrderBy(e => e.ProgramName)
            .ThenBy(e => e.FullName)
            .ToList();
    }

    public async Task<AttendanceNoteDto?> AddNoteAsync(Guid userId, Guid recordId, CreateAttendanceNoteDto dto)
    {
        var record = await _uow.Attendance.GetByIdAsync(recordId);
        if (record is null) return null;

        // As in UpdateRecordAsync: an unresolvable session is a 404, not a skipped check.
        var session = await _uow.Sessions.GetByIdAsync(record.SessionId);
        if (session is null) return null;

        (await _access.ForUserAsync(userId)).Require(session.ProgramId);

        var note = new AttendanceNote
        {
            AttendanceRecordId = recordId,
            Content = dto.Content.Trim(),
            NoteType = dto.NoteType,
        };
        await _uow.AttendanceNotes.AddAsync(note);
        await _uow.SaveChangesAsync();

        return new AttendanceNoteDto { Id = note.Id, Content = note.Content, NoteType = note.NoteType };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────
    //
    // Program scoping now lives in IProgramAccessService so participants, progress,
    // roster and planning enforce the same rule this service used to enforce alone (#1).

    private static MeetingDays ToFlag(DayOfWeek d) => d switch
    {
        DayOfWeek.Sunday => MeetingDays.Sunday,
        DayOfWeek.Monday => MeetingDays.Monday,
        DayOfWeek.Tuesday => MeetingDays.Tuesday,
        DayOfWeek.Wednesday => MeetingDays.Wednesday,
        DayOfWeek.Thursday => MeetingDays.Thursday,
        DayOfWeek.Friday => MeetingDays.Friday,
        DayOfWeek.Saturday => MeetingDays.Saturday,
        _ => MeetingDays.None,
    };

    private static string? FormatTimeRange(TimeOnly? start, TimeOnly? end)
    {
        if (start is null && end is null) return null;
        if (start is not null && end is not null) return $"{start:h:mm tt}–{end:h:mm tt}";
        return (start ?? end)?.ToString("h:mm tt");
    }
}
