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

        var participants = (await _uow.Participants.GetAllAsync())
            .Where(p => access.CanAccess(p.ProgramId))
            .ToList();
        if (programId is { } pid) participants = participants.Where(p => p.ProgramId == pid).ToList();

        var ctx = await LoadContextAsync(monthKey);

        return participants
            .Select(p => BuildDto(p, ctx.PlanByParticipant.GetValueOrDefault(p.Id), monthKey, ctx))
            .OrderBy(e => e.ProgramName).ThenBy(e => e.ParticipantName)
            .ToList();
    }

    public async Task<PerStarPlanDto?> UpsertPerStarPlanAsync(Guid userId, UpsertPerStarPlanDto dto)
    {
        var participant = await _access.RequireParticipantAsync(userId, dto.ParticipantId);
        if (participant is null) return null;

        var existing = (await _uow.PerStarPlans.ListAsync(
            p => p.ParticipantId == dto.ParticipantId && p.MonthKey == dto.MonthKey)).FirstOrDefault();

        if (existing is null)
        {
            existing = new PerStarPlan
            {
                ParticipantId = dto.ParticipantId,
                MonthKey = dto.MonthKey,
                AssignedStaffId = dto.AssignedStaffId,
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
            existing.AssignedStaffId = dto.AssignedStaffId;
            existing.PrimaryTier = dto.PrimaryTier;
            existing.PriorityObjectiveAreaId = dto.PriorityObjectiveAreaId;
            existing.PrioritySubSkillId = dto.PrioritySubSkillId;
            existing.MonthlyGoal = dto.MonthlyGoal;
            existing.HowIllSupport = dto.HowIllSupport;
            existing.Notes = dto.Notes;
            await _uow.PerStarPlans.UpdateAsync(existing);
        }

        await _uow.SaveChangesAsync();

        var ctx = await LoadContextAsync(dto.MonthKey);
        return BuildDto(participant, existing, dto.MonthKey, ctx);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private sealed record Ctx(
        Dictionary<Guid, CrmProgram> Programs,
        Dictionary<Guid, string> Staff,
        Dictionary<Guid, ObjectiveArea> Areas,
        Dictionary<Guid, SubSkill> SubSkills,
        Dictionary<Guid, PerStarPlan> PlanByParticipant,
        Dictionary<Guid, Guid> RosterStaffByParticipant);

    private async Task<Ctx> LoadContextAsync(string monthKey)
    {
        var programs = await _uow.Programs.GetAllAsync();
        var staff = await _uow.Staff.GetAllAsync();
        var areas = await _uow.ObjectiveAreas.GetAllAsync();
        var subSkills = await _uow.SubSkills.GetAllAsync();
        var plans = await _uow.PerStarPlans.ListAsync(p => p.MonthKey == monthKey);

        // The Roster's quarterly "Assigned staff" is the default for a plan that has not
        // named anyone yet — teachers expected their roster to carry over here (Sep 2026).
        var (year, quarter) = QuarterOf(monthKey);
        var roster = await _uow.RosterAssignments.ListAsync(r => r.Year == year && r.Quarter == quarter);
        var rosterStaff = roster
            .Where(r => r.AssignedStaffId is not null)
            .ToDictionary(r => r.ParticipantId, r => r.AssignedStaffId!.Value);

        return new Ctx(
            programs.ToDictionary(p => p.Id),
            staff.ToDictionary(s => s.Id, s => s.FullName),
            areas.ToDictionary(a => a.Id),
            subSkills.ToDictionary(s => s.Id),
            plans.ToDictionary(p => p.ParticipantId),
            rosterStaff);
    }

    /// <summary>"2026-09" → (2026, 3). Anything unparseable falls back to the current quarter.</summary>
    internal static (int Year, int Quarter) QuarterOf(string monthKey)
    {
        if (monthKey.Length >= 7 && int.TryParse(monthKey[..4], out var y) && int.TryParse(monthKey[5..7], out var m) && m is >= 1 and <= 12)
            return (y, (m - 1) / 3 + 1);
        var now = DateTime.UtcNow;
        return (now.Year, (now.Month - 1) / 3 + 1);
    }

    private static PerStarPlanDto BuildDto(Participant p, PerStarPlan? plan, string monthKey, Ctx ctx)
    {
        var program = ctx.Programs.GetValueOrDefault(p.ProgramId);
        var dto = new PerStarPlanDto
        {
            ParticipantId = p.Id,
            ParticipantName = p.FullName,
            ParticipantInitials = p.Initials,
            Status = p.Status,
            ProgramId = p.ProgramId,
            ProgramName = program?.Name ?? "",
            ProgramSlug = program?.Slug ?? "",
            MonthKey = monthKey,
        };

        // Plan's own choice first, else the quarter's roster assignment.
        var staffId = plan?.AssignedStaffId ?? (ctx.RosterStaffByParticipant.TryGetValue(p.Id, out var rs) ? rs : (Guid?)null);
        dto.AssignedStaffId = staffId;
        dto.AssignedStaffName = staffId is { } sid ? ctx.Staff.GetValueOrDefault(sid) : null;
        dto.AssignedStaffSource = staffId is null ? null : plan?.AssignedStaffId is not null ? "Plan" : "Roster";

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
}
