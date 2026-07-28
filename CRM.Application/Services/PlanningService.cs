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
        Dictionary<Guid, PerStarPlan> PlanByParticipant);

    private async Task<Ctx> LoadContextAsync(string monthKey)
    {
        var programs = await _uow.Programs.GetAllAsync();
        var staff = await _uow.Staff.GetAllAsync();
        var areas = await _uow.ObjectiveAreas.GetAllAsync();
        var subSkills = await _uow.SubSkills.GetAllAsync();
        var plans = await _uow.PerStarPlans.ListAsync(p => p.MonthKey == monthKey);
        return new Ctx(
            programs.ToDictionary(p => p.Id),
            staff.ToDictionary(s => s.Id, s => s.FullName),
            areas.ToDictionary(a => a.Id),
            subSkills.ToDictionary(s => s.Id),
            plans.ToDictionary(p => p.ParticipantId));
    }

    private static PerStarPlanDto BuildDto(Participant p, PerStarPlan? plan, string monthKey, Ctx ctx)
    {
        var program = ctx.Programs.GetValueOrDefault(p.ProgramId);
        var dto = new PerStarPlanDto
        {
            ParticipantId = p.Id,
            ParticipantName = p.FullName,
            ParticipantInitials = p.Initials,
            ProgramId = p.ProgramId,
            ProgramName = program?.Name ?? "",
            ProgramSlug = program?.Slug ?? "",
            MonthKey = monthKey,
        };

        if (plan is not null)
        {
            dto.PlanId = plan.Id;
            dto.AssignedStaffId = plan.AssignedStaffId;
            dto.AssignedStaffName = plan.AssignedStaffId is { } sid ? ctx.Staff.GetValueOrDefault(sid) : null;
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
