using System.Globalization;
using CRM.Application.DTOs.Progress;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

public class ProgressTrackingService : IProgressTrackingService
{
    private readonly IUnitOfWork _uow;
    private readonly IProgramAccessService _access;
    private readonly IOrgClock _clock;

    public ProgressTrackingService(IUnitOfWork uow, IProgramAccessService access, IOrgClock clock)
    {
        _uow = uow;
        _access = access;
        _clock = clock;
    }

    public async Task<IReadOnlyList<WeeklyFocusSkillDto>> GetFocusSkillsAsync(Guid currentUserId, Guid programId, string monthKey)
    {
        (await _access.ForUserAsync(currentUserId)).Require(programId);

        var focus = await _uow.WeeklyFocusSkills.ListAsync(f => f.ProgramId == programId && f.MonthKey == monthKey);
        var skills = await SubSkillMapAsync();
        return focus
            .OrderBy(f => f.WeekNumber)
            .ThenBy(f => skills.GetValueOrDefault(f.SubSkillId)?.SectionNumber ?? 0)
            .Select(f => ToFocusDto(f, skills))
            .ToList();
    }

    public async Task<IReadOnlyList<WeeklyFocusSkillDto>> SetFocusSkillsAsync(Guid currentUserId, SetFocusSkillsDto dto)
    {
        (await _access.ForUserAsync(currentUserId)).Require(dto.ProgramId);

        // Diff against what's stored instead of delete-all-then-reinsert (#28): unchanged
        // rows are left untouched, so a no-op save issues no writes at all.
        var existing = await _uow.WeeklyFocusSkills.ListAsync(
            f => f.ProgramId == dto.ProgramId && f.MonthKey == dto.MonthKey && f.WeekNumber == dto.WeekNumber);
        var wanted = dto.SubSkillIds.Distinct().ToHashSet();

        var kept = new List<WeeklyFocusSkill>();
        foreach (var old in existing)
        {
            if (wanted.Contains(old.SubSkillId)) kept.Add(old);
            else await _uow.WeeklyFocusSkills.DeleteAsync(old);
        }

        var keptIds = kept.Select(f => f.SubSkillId).ToHashSet();
        foreach (var subId in wanted.Where(id => !keptIds.Contains(id)))
        {
            kept.Add(await _uow.WeeklyFocusSkills.AddAsync(new WeeklyFocusSkill
            {
                ProgramId = dto.ProgramId,
                MonthKey = dto.MonthKey,
                WeekNumber = dto.WeekNumber,
                SubSkillId = subId,
            }));
        }

        await _uow.SaveChangesAsync();

        // Build the response from the entities in hand — no re-read (#28).
        var skills = await SubSkillMapAsync();
        return kept.Select(f => ToFocusDto(f, skills)).ToList();
    }

    public async Task<WeeklyDataEntryDto?> RecordWeeklyScoreAsync(Guid currentUserId, RecordWeeklyScoreDto dto)
    {
        if (await _access.RequireParticipantAsync(currentUserId, dto.ParticipantId) is null) return null;

        var recordedBy = await ResolveStaffIdAsync(currentUserId);

        var existing = (await _uow.WeeklyDataEntries.ListAsync(e =>
            e.ParticipantId == dto.ParticipantId && e.SubSkillId == dto.SubSkillId &&
            e.MonthKey == dto.MonthKey && e.WeekNumber == dto.WeekNumber)).FirstOrDefault();

        // A score recorded during an evening session belongs to that day locally, not to
        // the next UTC day (#7).
        var weekDate = ParseDate(dto.WeekDate) ?? _clock.Today;

        if (existing is null)
        {
            existing = new WeeklyDataEntry
            {
                ParticipantId = dto.ParticipantId,
                SubSkillId = dto.SubSkillId,
                SessionId = dto.SessionId,
                MonthKey = dto.MonthKey,
                WeekNumber = dto.WeekNumber,
                WeekDate = weekDate,
                Score = dto.Score,
                RecordedByStaffMemberId = recordedBy,
            };
            await _uow.WeeklyDataEntries.AddAsync(existing);
        }
        else
        {
            existing.Score = dto.Score;
            existing.WeekDate = weekDate;
            existing.SessionId = dto.SessionId;
            existing.RecordedByStaffMemberId = recordedBy;
            await _uow.WeeklyDataEntries.UpdateAsync(existing);
        }

        // Recompute this skill's month-end snapshot in the same save, so the Month-end
        // level is filled in the moment a score lands instead of waiting for someone to
        // find the "Recompute" button. Only the affected skill is touched — recomputing
        // the whole roster's snapshots on every keystroke would be wasted writes — and a
        // level a teacher has already confirmed is never overwritten.
        var snap = await RecomputeSkillSnapshotAsync(dto.ParticipantId, dto.SubSkillId, dto.MonthKey, existing);

        await _uow.SaveChangesAsync();

        var result = ToEntryDto(existing);
        result.Snapshot = ToSnapshotDto(snap, await SubSkillMapAsync());
        return result;
    }

    public async Task<SaveWeeklyScoresResultDto> SaveWeeklyScoresAsync(Guid currentUserId, SaveWeeklyScoresDto dto)
    {
        var result = new SaveWeeklyScoresResultDto();
        if (dto.Changes.Count == 0) return result;

        // Scope check up front for every star in the batch — a teacher's grid never mixes
        // programs they cannot see, so a failure here is a bug or a forged request, and
        // either way nothing should be half-written.
        var known = new HashSet<Guid>();
        foreach (var pid in dto.Changes.Select(c => c.ParticipantId).Distinct())
            if (await _access.RequireParticipantAsync(currentUserId, pid) is not null) known.Add(pid);
        dto.Changes.RemoveAll(c => !known.Contains(c.ParticipantId));
        if (dto.Changes.Count == 0) return result;

        var recordedBy = await ResolveStaffIdAsync(currentUserId);
        var weekDate = ParseDate(dto.WeekDate) ?? _clock.Today;
        var thresholds = (await _uow.ScoreThresholds.GetAllAsync()).Select(t => (t.Level, t.MinAverage)).ToList();

        var starIds = dto.Changes.Select(c => c.ParticipantId).Distinct().ToHashSet();
        var monthEntries = (await _uow.WeeklyDataEntries.ListAsync(e => e.MonthKey == dto.MonthKey && starIds.Contains(e.ParticipantId))).ToList();
        var monthSnaps = (await _uow.MonthlyProgressSnapshots.ListAsync(s => s.MonthKey == dto.MonthKey && starIds.Contains(s.ParticipantId))).ToList();

        // Last change to a cell wins, so a teacher who clicked 2 then 3 gets 3 — the bug the
        // per-cell autosave had was exactly that this was not guaranteed.
        var changes = dto.Changes
            .GroupBy(c => (c.ParticipantId, c.SubSkillId, c.WeekNumber))
            .Select(g => g.Last())
            .ToList();

        foreach (var c in changes)
        {
            var existing = monthEntries.FirstOrDefault(e => e.ParticipantId == c.ParticipantId && e.SubSkillId == c.SubSkillId && e.WeekNumber == c.WeekNumber);
            if (c.Score is null)
            {
                if (existing is null) continue;
                await _uow.WeeklyDataEntries.DeleteAsync(existing);
                monthEntries.Remove(existing);
            }
            else if (existing is null)
            {
                var entry = new WeeklyDataEntry
                {
                    ParticipantId = c.ParticipantId,
                    SubSkillId = c.SubSkillId,
                    MonthKey = dto.MonthKey,
                    WeekNumber = c.WeekNumber,
                    WeekDate = weekDate,
                    Score = c.Score.Value,
                    RecordedByStaffMemberId = recordedBy,
                };
                await _uow.WeeklyDataEntries.AddAsync(entry);
                monthEntries.Add(entry);
            }
            else
            {
                existing.Score = c.Score.Value;
                existing.WeekDate = weekDate;
                existing.RecordedByStaffMemberId = recordedBy;
                await _uow.WeeklyDataEntries.UpdateAsync(existing);
            }
        }

        // One snapshot refresh per (star, skill) touched, from the in-memory month.
        foreach (var (pid, sid) in changes.Select(c => (c.ParticipantId, c.SubSkillId)).Distinct())
        {
            var scores = monthEntries.Where(e => e.ParticipantId == pid && e.SubSkillId == sid).Select(e => e.Score);
            var r = ProgressLevelCalculator.Derive(scores, thresholds);
            var snap = monthSnaps.FirstOrDefault(s => s.ParticipantId == pid && s.SubSkillId == sid);
            if (snap is null)
            {
                snap = new MonthlyProgressSnapshot
                {
                    ParticipantId = pid, SubSkillId = sid, MonthKey = dto.MonthKey,
                    SuggestedLevel = r.Level, Level = r.Level, SummedScore = r.SummedScore, ScoredWeekCount = r.ScoredWeekCount, IsConfirmed = false,
                };
                await _uow.MonthlyProgressSnapshots.AddAsync(snap);
                monthSnaps.Add(snap);
            }
            else
            {
                snap.SuggestedLevel = r.Level;
                snap.SummedScore = r.SummedScore;
                snap.ScoredWeekCount = r.ScoredWeekCount;
                if (!snap.IsConfirmed) snap.Level = r.Level;
                await _uow.MonthlyProgressSnapshots.UpdateAsync(snap);
            }
        }

        await _uow.SaveChangesAsync();

        var skills = await SubSkillMapAsync();
        var touched = changes.Select(c => (c.ParticipantId, c.SubSkillId)).ToHashSet();
        result.Entries = monthEntries.Where(e => touched.Contains((e.ParticipantId, e.SubSkillId))).OrderBy(e => e.WeekNumber).Select(ToEntryDto).ToList();
        result.Snapshots = monthSnaps.Where(s => touched.Contains((s.ParticipantId, s.SubSkillId))).Select(s => ToSnapshotDto(s, skills)).ToList();
        return result;
    }

    /// <summary>
    /// Refreshes the MonthlyProgressSnapshot for one (participant, skill, month) from its
    /// weekly entries. <paramref name="justWritten"/> is the entry the caller has staged but
    /// not yet saved — a database query cannot see it, so it is merged in by hand.
    /// Does not save; the caller owns the transaction.
    /// </summary>
    private async Task<MonthlyProgressSnapshot> RecomputeSkillSnapshotAsync(
        Guid participantId, Guid subSkillId, string monthKey, WeeklyDataEntry justWritten)
    {
        var monthEntries = await _uow.WeeklyDataEntries.ListAsync(e =>
            e.ParticipantId == participantId && e.SubSkillId == subSkillId && e.MonthKey == monthKey);
        var scores = monthEntries
            .Where(e => e.Id != justWritten.Id)
            .Select(e => e.Score)
            .Append(justWritten.Score);

        var thresholds = (await _uow.ScoreThresholds.GetAllAsync())
            .Select(t => (t.Level, t.MinAverage)).ToList();
        var r = ProgressLevelCalculator.Derive(scores, thresholds);

        var snap = (await _uow.MonthlyProgressSnapshots.ListAsync(s =>
            s.ParticipantId == participantId && s.SubSkillId == subSkillId && s.MonthKey == monthKey))
            .FirstOrDefault();

        if (snap is null)
        {
            snap = new MonthlyProgressSnapshot
            {
                ParticipantId = participantId,
                SubSkillId = subSkillId,
                MonthKey = monthKey,
                SuggestedLevel = r.Level,
                Level = r.Level,
                SummedScore = r.SummedScore,
                ScoredWeekCount = r.ScoredWeekCount,
                IsConfirmed = false,
            };
            await _uow.MonthlyProgressSnapshots.AddAsync(snap);
        }
        else
        {
            snap.SuggestedLevel = r.Level;
            snap.SummedScore = r.SummedScore;
            snap.ScoredWeekCount = r.ScoredWeekCount;
            if (!snap.IsConfirmed) snap.Level = r.Level; // same rule as ComputeMonthEndAsync
            await _uow.MonthlyProgressSnapshots.UpdateAsync(snap);
        }

        return snap;
    }

    public async Task<IReadOnlyList<WeeklyDataEntryDto>> GetProgramMonthAsync(Guid currentUserId, Guid programId, string monthKey)
    {
        (await _access.ForUserAsync(currentUserId)).Require(programId);

        var ids = (await _uow.Participants.ListAsync(p => p.ProgramId == programId || p.SecondaryProgramId == programId))
            .Select(p => p.Id)
            .ToHashSet();
        if (ids.Count == 0) return [];

        var entries = await _uow.WeeklyDataEntries.ListAsync(e => e.MonthKey == monthKey && ids.Contains(e.ParticipantId));
        return entries.OrderBy(e => e.WeekNumber).Select(ToEntryDto).ToList();
    }

    public async Task<StarMonthDto?> GetStarMonthAsync(Guid currentUserId, Guid participantId, string monthKey)
    {
        if (await _access.RequireParticipantAsync(currentUserId, participantId) is null) return null;

        var entries = await _uow.WeeklyDataEntries.ListAsync(e => e.ParticipantId == participantId && e.MonthKey == monthKey);
        var snaps = await _uow.MonthlyProgressSnapshots.ListAsync(s => s.ParticipantId == participantId && s.MonthKey == monthKey);
        var notes = await _uow.WeeklyNoteSelections.ListAsync(n => n.ParticipantId == participantId && n.MonthKey == monthKey);
        var summary = (await _uow.MonthlySummaries.ListAsync(m => m.ParticipantId == participantId && m.MonthKey == monthKey)).FirstOrDefault();
        var skills = await SubSkillMapAsync();
        var bankText = await GoalBankTextMapAsync();

        // Overall level for the month: every weekly score the Star received, pooled across
        // skills and averaged once. Derive already excludes N/A, so a skill marked
        // not-applicable does not drag the average down.
        var thresholds = (await _uow.ScoreThresholds.GetAllAsync())
            .Select(t => (t.Level, t.MinAverage)).ToList();
        var overall = ProgressLevelCalculator.Derive(entries.Select(e => e.Score), thresholds);

        return new StarMonthDto
        {
            ParticipantId = participantId,
            MonthKey = monthKey,
            Entries = entries
                .OrderBy(e => e.WeekNumber)
                .Select(ToEntryDto)
                .ToList(),
            Snapshots = snaps
                .OrderBy(s => skills.GetValueOrDefault(s.SubSkillId)?.SectionNumber ?? 0)
                .ThenBy(s => skills.GetValueOrDefault(s.SubSkillId)?.SortOrder ?? 0)
                .Select(s => ToSnapshotDto(s, skills))
                .ToList(),
            NoteSelections = notes
                .OrderBy(n => n.WeekNumber).ThenBy(n => n.Kind)
                .Select(n => ToNoteDto(n, bankText))
                .ToList(),
            MonthlySummary = summary is null ? null : ToSummaryDto(summary),
            SuggestedPrimaryLevel = overall.Level,
            SuggestedPrimaryScoredCount = overall.ScoredWeekCount,
        };
    }

    public async Task<WeeklyNoteSelectionDto?> UpsertNoteSelectionAsync(Guid currentUserId, Guid participantId, string monthKey, UpsertNoteSelectionDto dto)
    {
        if (await _access.RequireParticipantAsync(currentUserId, participantId) is null) return null;

        var note = (await _uow.WeeklyNoteSelections.ListAsync(n =>
            n.ParticipantId == participantId && n.MonthKey == monthKey &&
            n.WeekNumber == dto.WeekNumber && n.Kind == dto.Kind)).FirstOrDefault();

        var custom = string.IsNullOrWhiteSpace(dto.CustomText) ? null : dto.CustomText.Trim();

        if (note is null)
        {
            note = new WeeklyNoteSelection
            {
                ParticipantId = participantId,
                MonthKey = monthKey,
                WeekNumber = dto.WeekNumber,
                Kind = dto.Kind,
                GoalBankEntryId = dto.GoalBankEntryId,
                CustomText = custom,
            };
            await _uow.WeeklyNoteSelections.AddAsync(note);
        }
        else
        {
            note.GoalBankEntryId = dto.GoalBankEntryId;
            note.CustomText = custom;
            await _uow.WeeklyNoteSelections.UpdateAsync(note);
        }

        await _uow.SaveChangesAsync();
        return ToNoteDto(note, await GoalBankTextMapAsync());
    }

    public async Task<MonthlySummaryDto?> UpsertMonthlySummaryAsync(Guid currentUserId, Guid participantId, string monthKey, UpsertMonthlySummaryDto dto)
    {
        if (await _access.RequireParticipantAsync(currentUserId, participantId) is null) return null;

        var summary = (await _uow.MonthlySummaries.ListAsync(m => m.ParticipantId == participantId && m.MonthKey == monthKey)).FirstOrDefault();

        if (summary is null)
        {
            summary = new MonthlySummary
            {
                ParticipantId = participantId,
                MonthKey = monthKey,
                PrimaryLevel = dto.PrimaryLevel,
                ProgressNarrative = string.IsNullOrWhiteSpace(dto.ProgressNarrative) ? null : dto.ProgressNarrative.Trim(),
                GoalsCarryOver = dto.GoalsCarryOver,
                NextMonthUpdate = string.IsNullOrWhiteSpace(dto.NextMonthUpdate) ? null : dto.NextMonthUpdate.Trim(),
            };
            await _uow.MonthlySummaries.AddAsync(summary);
        }
        else
        {
            summary.PrimaryLevel = dto.PrimaryLevel;
            summary.ProgressNarrative = string.IsNullOrWhiteSpace(dto.ProgressNarrative) ? null : dto.ProgressNarrative.Trim();
            summary.GoalsCarryOver = dto.GoalsCarryOver;
            summary.NextMonthUpdate = string.IsNullOrWhiteSpace(dto.NextMonthUpdate) ? null : dto.NextMonthUpdate.Trim();
            await _uow.MonthlySummaries.UpdateAsync(summary);
        }

        await _uow.SaveChangesAsync();
        return ToSummaryDto(summary);
    }

    public async Task<IReadOnlyList<MonthlyProgressSnapshotDto>?> ComputeMonthEndAsync(Guid currentUserId, Guid participantId, string monthKey)
    {
        if (await _access.RequireParticipantAsync(currentUserId, participantId) is null) return null;

        var skills = (await _uow.SubSkills.GetAllAsync()).Where(s => s.IsActive).ToList();
        var entries = await _uow.WeeklyDataEntries.ListAsync(e => e.ParticipantId == participantId && e.MonthKey == monthKey);
        var thresholds = (await _uow.ScoreThresholds.GetAllAsync()).Select(t => (t.Level, t.MinAverage)).ToList();

        var snaps = (await _uow.MonthlyProgressSnapshots.ListAsync(s => s.ParticipantId == participantId && s.MonthKey == monthKey)).ToList();
        var bySkill = snaps.ToDictionary(s => s.SubSkillId);
        var entriesBySkill = entries.GroupBy(e => e.SubSkillId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var skill in skills)
        {
            var scores = entriesBySkill.TryGetValue(skill.Id, out var es)
                ? es.Select(e => e.Score)
                : Enumerable.Empty<Domain.Enums.DataScore>();
            var r = ProgressLevelCalculator.Derive(scores, thresholds);

            if (bySkill.TryGetValue(skill.Id, out var snap))
            {
                snap.SuggestedLevel = r.Level;
                snap.SummedScore = r.SummedScore;
                snap.ScoredWeekCount = r.ScoredWeekCount;
                if (!snap.IsConfirmed) snap.Level = r.Level; // never overwrite a confirmed level
                await _uow.MonthlyProgressSnapshots.UpdateAsync(snap);
            }
            else
            {
                snap = new MonthlyProgressSnapshot
                {
                    ParticipantId = participantId,
                    SubSkillId = skill.Id,
                    MonthKey = monthKey,
                    SuggestedLevel = r.Level,
                    Level = r.Level,
                    SummedScore = r.SummedScore,
                    ScoredWeekCount = r.ScoredWeekCount,
                    IsConfirmed = false,
                };
                await _uow.MonthlyProgressSnapshots.AddAsync(snap);
                snaps.Add(snap);
            }
        }

        await _uow.SaveChangesAsync();

        var skillMap = await SubSkillMapAsync();
        return snaps
            .OrderBy(s => skillMap.GetValueOrDefault(s.SubSkillId)?.SectionNumber ?? 0)
            .ThenBy(s => skillMap.GetValueOrDefault(s.SubSkillId)?.SortOrder ?? 0)
            .Select(s => ToSnapshotDto(s, skillMap))
            .ToList();
    }

    public async Task<MonthlyProgressSnapshotDto?> ConfirmMonthEndAsync(Guid currentUserId, Guid participantId, string monthKey, ConfirmMonthEndDto dto)
    {
        if (await _access.RequireParticipantAsync(currentUserId, participantId) is null) return null;
        var confirmedBy = await ResolveStaffIdAsync(currentUserId);

        var snap = (await _uow.MonthlyProgressSnapshots.ListAsync(s =>
            s.ParticipantId == participantId && s.MonthKey == monthKey && s.SubSkillId == dto.SubSkillId)).FirstOrDefault();

        if (snap is null)
        {
            snap = new MonthlyProgressSnapshot
            {
                ParticipantId = participantId,
                SubSkillId = dto.SubSkillId,
                MonthKey = monthKey,
                Level = dto.Level,
                SuggestedLevel = dto.Level,
                IsConfirmed = true,
                ConfirmedByStaffMemberId = confirmedBy,
            };
            await _uow.MonthlyProgressSnapshots.AddAsync(snap);
        }
        else
        {
            snap.Level = dto.Level;
            snap.IsConfirmed = true;
            snap.ConfirmedByStaffMemberId = confirmedBy;
            await _uow.MonthlyProgressSnapshots.UpdateAsync(snap);
        }

        await _uow.SaveChangesAsync();
        return ToSnapshotDto(snap, await SubSkillMapAsync());
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>The caller's linked staff-member id (null for accounts not linked to staff, e.g. pure admins).</summary>
    private async Task<Guid?> ResolveStaffIdAsync(Guid userId) =>
        (await _uow.Users.GetByIdAsync(userId))?.StaffMemberId;

    private async Task<Dictionary<Guid, SubSkill>> SubSkillMapAsync() =>
        (await _uow.SubSkills.GetAllAsync()).ToDictionary(s => s.Id);

    private async Task<Dictionary<Guid, string>> GoalBankTextMapAsync() =>
        (await _uow.GoalBankEntries.GetAllAsync()).ToDictionary(g => g.Id, g => g.Text);

    private static WeeklyNoteSelectionDto ToNoteDto(WeeklyNoteSelection n, Dictionary<Guid, string> bankText) => new()
    {
        Id = n.Id,
        ParticipantId = n.ParticipantId,
        MonthKey = n.MonthKey,
        WeekNumber = n.WeekNumber,
        Kind = n.Kind,
        GoalBankEntryId = n.GoalBankEntryId,
        CustomText = n.CustomText,
        DisplayText = n.GoalBankEntryId is { } id && bankText.TryGetValue(id, out var t) ? t : n.CustomText,
    };

    private static MonthlySummaryDto ToSummaryDto(MonthlySummary s) => new()
    {
        ParticipantId = s.ParticipantId,
        MonthKey = s.MonthKey,
        PrimaryLevel = s.PrimaryLevel,
        ProgressNarrative = s.ProgressNarrative,
        GoalsCarryOver = s.GoalsCarryOver,
        NextMonthUpdate = s.NextMonthUpdate,
        HasSummary = true,
    };

    private static DateTime? ParseDate(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    private static WeeklyDataEntryDto ToEntryDto(WeeklyDataEntry e) => new()
    {
        Id = e.Id,
        ParticipantId = e.ParticipantId,
        SubSkillId = e.SubSkillId,
        SessionId = e.SessionId,
        MonthKey = e.MonthKey,
        WeekNumber = e.WeekNumber,
        WeekDate = e.WeekDate.ToString("yyyy-MM-dd"),
        Score = e.Score,
        RecordedByStaffMemberId = e.RecordedByStaffMemberId,
    };

    private static MonthlyProgressSnapshotDto ToSnapshotDto(MonthlyProgressSnapshot s, Dictionary<Guid, SubSkill> skills)
    {
        var skill = skills.GetValueOrDefault(s.SubSkillId);
        return new MonthlyProgressSnapshotDto
        {
            Id = s.Id,
            ParticipantId = s.ParticipantId,
            SubSkillId = s.SubSkillId,
            SubSkillName = skill?.Name ?? "",
            SectionNumber = skill?.SectionNumber ?? 0,
            MonthKey = s.MonthKey,
            Level = s.Level,
            SuggestedLevel = s.SuggestedLevel,
            SummedScore = s.SummedScore,
            ScoredWeekCount = s.ScoredWeekCount,
            IsConfirmed = s.IsConfirmed,
            ConfirmedByStaffMemberId = s.ConfirmedByStaffMemberId,
        };
    }

    private static WeeklyFocusSkillDto ToFocusDto(WeeklyFocusSkill f, Dictionary<Guid, SubSkill> skills)
    {
        var skill = skills.GetValueOrDefault(f.SubSkillId);
        return new WeeklyFocusSkillDto
        {
            ProgramId = f.ProgramId,
            MonthKey = f.MonthKey,
            WeekNumber = f.WeekNumber,
            SubSkillId = f.SubSkillId,
            SubSkillName = skill?.Name ?? "",
            SectionNumber = skill?.SectionNumber ?? 0,
        };
    }
}
