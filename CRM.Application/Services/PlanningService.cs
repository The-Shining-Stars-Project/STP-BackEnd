using CRM.Application.DTOs.Planning;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

public class PlanningService : IPlanningService
{
    private readonly IUnitOfWork _uow;
    private readonly IProgramAccessService _access;

    public PlanningService(IUnitOfWork uow, IProgramAccessService access)
    {
        _uow = uow;
        _access = access;
    }

    public async Task<IReadOnlyList<PerStarPlanDto>> GetPerStarPlansAsync(Guid userId, string monthKey, Guid? programId)
    {
        // Plans name a child's goals and support needs, so the list is scoped to the
        // caller's programs (#1) before the optional single-program narrowing is applied.
        var access = await _access.ForUserAsync(userId);
        if (programId is { } requested) access.Require(requested);

        // Either enrolment counts: a Part-time teacher plans for a dual-enrolled star whose
        // primary program is Pathways (Oct 2026). Still one row per star, since the plan is
        // one per star per month; the row is shown under the program it was found through.
        var participants = (await _uow.Participants.GetAllAsync())
            .Where(access.CanAccessStar)
            .ToList();
        if (programId is { } pid) participants = participants.Where(p => p.IsEnrolledIn(pid)).ToList();

        var ctx = await LoadContextAsync(monthKey);

        return participants
            .Select(p => BuildDto(p, RowProgram(p, access, programId), ctx.PlanByParticipant.GetValueOrDefault(p.Id), monthKey, ctx))
            .OrderBy(e => e.ProgramName).ThenBy(e => e.ParticipantName)
            .ToList();
    }

    public async Task<PerStarPlanDto?> UpsertPerStarPlanAsync(Guid userId, UpsertPerStarPlanDto dto)
    {
        var participant = await _access.RequireParticipantAsync(userId, dto.ParticipantId);
        if (participant is null) return null;

        // The save is made from a row shown under one of the star's programs. A dual-enrolled
        // star listed under their second program must come back labelled and resolved for that
        // program, or the card swaps to the other enrolment's roster teacher (Oct 2026).
        var access = await _access.ForUserAsync(userId);
        if (dto.ProgramId is { } requested)
        {
            if (!participant.IsEnrolledIn(requested))
                throw new ArgumentException("This star isn't enrolled in that program.");
            access.Require(requested);
        }
        var rowProgram = RowProgram(participant, access, dto.ProgramId);
        var ctx = await LoadContextAsync(dto.MonthKey);

        var existing = (await _uow.PerStarPlans.ListAsync(
            p => p.ParticipantId == dto.ParticipantId && p.MonthKey == dto.MonthKey)).FirstOrDefault();

        // A dual-enrolled star's plan can name a teacher of their OTHER program. This row never
        // showed that name (see ResolveAssignedStaff), so the "none" it sends back means "nothing
        // picked here", not "clear it" — keep it, or a Part-time tier edit would wipe the
        // Pathways teacher's pick.
        var assignedStaffId = dto.AssignedStaffId;
        if (assignedStaffId is null && existing?.AssignedStaffId is { } kept && !FitsRow(participant, kept, rowProgram, ctx))
            assignedStaffId = kept;

        // The reverse: a name this row could never show would be stored but hidden, so the pick
        // looks like it did nothing ("button does nothing"). Say why instead.
        if (dto.AssignedStaffId is { } picked && !FitsRow(participant, picked, rowProgram, ctx))
        {
            var staffName = ctx.Staff.GetValueOrDefault(picked)?.FullName ?? "That staff member";
            var programName = ctx.Programs.GetValueOrDefault(rowProgram)?.Name ?? "this program";
            throw new ArgumentException(
                $"{staffName} isn't assigned to {programName}, so they can't be this star's teacher there. " +
                "Add them to the program first (Programs → Manage staff).");
        }

        if (existing is null)
        {
            existing = new PerStarPlan
            {
                ParticipantId = dto.ParticipantId,
                MonthKey = dto.MonthKey,
                AssignedStaffId = assignedStaffId,
                PrimaryTier = dto.PrimaryTier,
                PriorityObjectiveAreaId = dto.PriorityObjectiveAreaId,
                PrioritySubSkillId = dto.PrioritySubSkillId,
                MonthlyGoal = dto.MonthlyGoal,
                HowIllSupport = dto.HowIllSupport,
                Notes = dto.Notes,
            };
            await _uow.PerStarPlans.AddAsync(existing);
        }
        else
        {
            existing.AssignedStaffId = assignedStaffId;
            existing.PrimaryTier = dto.PrimaryTier;
            existing.PriorityObjectiveAreaId = dto.PriorityObjectiveAreaId;
            existing.PrioritySubSkillId = dto.PrioritySubSkillId;
            existing.MonthlyGoal = dto.MonthlyGoal;
            existing.HowIllSupport = dto.HowIllSupport;
            existing.Notes = dto.Notes;
            await _uow.PerStarPlans.UpdateAsync(existing);
        }

        await _uow.SaveChangesAsync();

        // The context was loaded before the save, so the saved plan is passed in directly; its
        // staff, roster and earlier-term data are unchanged by this write.
        return BuildDto(participant, rowProgram, existing, dto.MonthKey, ctx);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The program a star's row is shown under: the requested program, otherwise the primary
    /// when the caller can see it, otherwise the secondary (the only way they can see the star).
    /// </summary>
    private static Guid RowProgram(Participant p, ProgramAccess access, Guid? requested) =>
        requested ?? (access.CanAccess(p.ProgramId) ? p.ProgramId : p.SecondaryProgramId ?? p.ProgramId);

    private sealed record Ctx(
        Dictionary<Guid, CrmProgram> Programs,
        Dictionary<Guid, StaffMember> Staff,
        Dictionary<Guid, ObjectiveArea> Areas,
        Dictionary<Guid, SubSkill> SubSkills,
        Dictionary<Guid, PerStarPlan> PlanByParticipant,
        int Year,
        int Quarter,
        Dictionary<(Guid ParticipantId, Guid ProgramId), Guid> RosterStaffByEnrollment,
        Dictionary<Guid, (Guid StaffId, string MonthKey)> PreviousPlanStaff,
        Dictionary<(Guid ParticipantId, Guid ProgramId), (Guid StaffId, int Year, int Quarter)> PreviousRosterStaff,
        HashSet<(Guid StaffId, Guid ProgramId)> StaffPrograms);

    private async Task<Ctx> LoadContextAsync(string monthKey)
    {
        var programs = await _uow.Programs.GetAllAsync();
        var staff = (await _uow.Staff.GetAllAsync()).ToDictionary(s => s.Id);
        var areas = await _uow.ObjectiveAreas.GetAllAsync();
        var subSkills = await _uow.SubSkills.GetAllAsync();
        var plans = await _uow.PerStarPlans.ListAsync(p => p.MonthKey == monthKey);
        var staffPrograms = (await _uow.GetStaffProgramAssignmentsAsync())
            .Select(a => (a.StaffMemberId, a.ProgramId))
            .ToHashSet();

        // The Roster's quarterly "Assigned staff" is the default for a plan that has not
        // named anyone yet — teachers expected their roster to carry over here (Sep 2026).
        // Roster rows are per enrolment, so a row uses the placement for the program it is
        // shown under. This quarter and every earlier one load together; never a later one.
        var (year, quarter) = QuarterOf(monthKey);
        var roster = await _uow.RosterAssignments.ListAsync(r =>
            r.AssignedStaffId != null && (r.Year < year || (r.Year == year && r.Quarter <= quarter)));
        var rosterStaff = roster
            .Where(r => r.Year == year && r.Quarter == quarter)
            .ToDictionary(r => (r.ParticipantId, r.ProgramId), r => r.AssignedStaffId!.Value);

        // Months and quarters roll over with nothing set (Oct 1 2026 blanked every Assigned
        // staff), so when this term is empty the latest EARLIER plan or roster row is shown
        // as a "from Q3"-style hint until someone sets this term. Only the LATEST earlier row
        // is kept here, former staff included: if that owner has since left, the star has no
        // carry-over (ResolveAssignedStaff drops it), matching copy-forward, which leaves the
        // star unassigned rather than reaching back to a teacher from months ago.
        var previousRoster = roster
            .Where(r => r.Year < year || (r.Year == year && r.Quarter < quarter))
            .GroupBy(r => (r.ParticipantId, r.ProgramId))
            .ToDictionary(g => g.Key, g =>
            {
                var latest = g.OrderByDescending(r => r.Year).ThenByDescending(r => r.Quarter).First();
                return (latest.AssignedStaffId!.Value, latest.Year, latest.Quarter);
            });

        var previousPlan = new Dictionary<Guid, (Guid, string)>();
        if (IsMonthKey(monthKey))
        {
            // "yyyy-MM" sorts as text, so an earlier month is a smaller string.
            var earlier = await _uow.PerStarPlans.ListAsync(p =>
                p.AssignedStaffId != null && string.Compare(p.MonthKey, monthKey) < 0);
            previousPlan = earlier
                .GroupBy(p => p.ParticipantId)
                .ToDictionary(g => g.Key, g =>
                {
                    var latest = g.OrderByDescending(p => p.MonthKey, StringComparer.Ordinal).First();
                    return (latest.AssignedStaffId!.Value, latest.MonthKey);
                });
        }

        return new Ctx(
            programs.ToDictionary(p => p.Id),
            staff,
            areas.ToDictionary(a => a.Id),
            subSkills.ToDictionary(s => s.Id),
            plans.ToDictionary(p => p.ParticipantId),
            year,
            quarter,
            rosterStaff,
            previousPlan,
            previousRoster,
            staffPrograms);
    }

    private static bool IsMonthKey(string monthKey) =>
        monthKey.Length == 7 && monthKey[4] == '-'
        && int.TryParse(monthKey[..4], out _)
        && int.TryParse(monthKey[5..7], out var m) && m is >= 1 and <= 12;

    /// <summary>"Q3 2026" — how the Roster names a term.</summary>
    internal static string QuarterTerm(int year, int quarter) => $"Q{quarter} {year}";

    /// <summary>"2026-09" → (2026, 3). Anything unparseable falls back to the current quarter.</summary>
    internal static (int Year, int Quarter) QuarterOf(string monthKey)
    {
        if (monthKey.Length >= 7 && int.TryParse(monthKey[..4], out var y) && int.TryParse(monthKey[5..7], out var m) && m is >= 1 and <= 12)
            return (y, (m - 1) / 3 + 1);
        var now = DateTime.UtcNow;
        return (now.Year, (now.Month - 1) / 3 + 1);
    }

    private static PerStarPlanDto BuildDto(Participant p, Guid programId, PerStarPlan? plan, string monthKey, Ctx ctx)
    {
        var program = ctx.Programs.GetValueOrDefault(programId);
        var dto = new PerStarPlanDto
        {
            ParticipantId = p.Id,
            ParticipantName = p.FullName,
            ParticipantInitials = p.Initials,
            Status = p.Status,
            ProgramId = programId,
            ProgramName = program?.Name ?? "",
            ProgramSlug = program?.Slug ?? "",
            IsSecondaryEnrollment = programId != p.ProgramId,
            IsDualEnrolled = p.SecondaryProgramId is not null,
            MonthKey = monthKey,
        };

        var (staffId, source, term) = ResolveAssignedStaff(p, programId, plan, monthKey, ctx);
        dto.AssignedStaffId = staffId;
        dto.AssignedStaffName = staffId is { } sid ? ctx.Staff.GetValueOrDefault(sid)?.FullName : null;
        dto.AssignedStaffSource = source;
        dto.AssignedStaffSourceTerm = term;

        if (plan is not null)
        {
            dto.PlanId = plan.Id;
            dto.PrimaryTier = plan.PrimaryTier;
            dto.PriorityObjectiveAreaId = plan.PriorityObjectiveAreaId;
            dto.PriorityObjectiveAreaName = plan.PriorityObjectiveAreaId is { } aid ? ctx.Areas.GetValueOrDefault(aid)?.Name : null;
            dto.PrioritySubSkillId = plan.PrioritySubSkillId;
            dto.PrioritySubSkillName = plan.PrioritySubSkillId is { } skid ? ctx.SubSkills.GetValueOrDefault(skid)?.Name : null;
            dto.MonthlyGoal = plan.MonthlyGoal;
            dto.HowIllSupport = plan.HowIllSupport;
            dto.Notes = plan.Notes;
        }
        return dto;
    }

    /// <summary>
    /// Whether a plan's named staff member can stand for this row. The plan is one per star,
    /// but a dual-enrolled star has a different teacher in each program (Chris: Pat on Pathways,
    /// Pam on Part-time), so their row only uses a plan name that is assigned to the row's
    /// program. A single-program star's plan always applies.
    /// </summary>
    private static bool FitsRow(Participant p, Guid staffId, Guid programId, Ctx ctx) =>
        p.SecondaryProgramId is null || ctx.StaffPrograms.Contains((staffId, programId));

    /// <summary>
    /// This month's plan (if it fits the row), then this quarter's roster for the row's program,
    /// then whichever carried-over hint is from the MORE RECENT term: the latest earlier plan or
    /// the latest earlier roster row for the row's program (a tie goes to the plan). Each hint is
    /// the latest earlier row, dropped if its staff member has left (or, for the plan, doesn't fit
    /// the row) — never swapped for an older one. A later term is never used.
    /// </summary>
    private static (Guid? StaffId, string? Source, string? Term) ResolveAssignedStaff(
        Participant p, Guid programId, PerStarPlan? plan, string monthKey, Ctx ctx)
    {
        if (plan?.AssignedStaffId is { } planned && FitsRow(p, planned, programId, ctx))
            return (planned, "Plan", monthKey);
        if (ctx.RosterStaffByEnrollment.TryGetValue((p.Id, programId), out var rostered))
            return (rostered, "Roster", QuarterTerm(ctx.Year, ctx.Quarter));

        // A carried-over name is a hint, so it must be someone still on staff.
        bool IsCurrent(Guid staffId) => ctx.Staff.TryGetValue(staffId, out var s) && s.EndDate is null;

        (Guid StaffId, string MonthKey)? prevPlan =
            ctx.PreviousPlanStaff.TryGetValue(p.Id, out var pp) && FitsRow(p, pp.StaffId, programId, ctx)
                ? pp : null;
        (Guid StaffId, int Year, int Quarter)? prevRoster =
            ctx.PreviousRosterStaff.TryGetValue((p.Id, programId), out var pr) ? pr : null;

        // Pick the most recent hint FIRST, then check the person is still on staff. Checking
        // first would let an April plan stand in for a Q3 roster teacher who has since left —
        // the "reach back months" miscount copy-forward avoids by leaving the star unassigned.
        // A plan from April must not beat a Q3 roster row either: compare quarter to quarter.
        if (prevPlan is { } plannedBefore && prevRoster is { } rosteredBefore)
        {
            var (planYear, planQuarter) = QuarterOf(plannedBefore.MonthKey);
            var rosterIsNewer = rosteredBefore.Year > planYear
                || (rosteredBefore.Year == planYear && rosteredBefore.Quarter > planQuarter);
            if (rosterIsNewer) prevPlan = null; else prevRoster = null;
        }

        if (prevPlan is { } fromPlan)
            return IsCurrent(fromPlan.StaffId) ? (fromPlan.StaffId, "PreviousPlan", fromPlan.MonthKey) : (null, null, null);
        if (prevRoster is { } fromRoster)
            return IsCurrent(fromRoster.StaffId)
                ? (fromRoster.StaffId, "PreviousRoster", QuarterTerm(fromRoster.Year, fromRoster.Quarter))
                : (null, null, null);
        return (null, null, null);
    }
}
