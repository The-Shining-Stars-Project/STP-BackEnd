using CRM.Application.DTOs.Participants;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

public class ParticipantService : IParticipantService
{
    private readonly IUnitOfWork _uow;
    private readonly IStatsQueries _stats;
    private readonly IProgramAccessService _access;

    public ParticipantService(IUnitOfWork uow, IStatsQueries stats, IProgramAccessService access)
    {
        _uow = uow;
        _stats = stats;
        _access = access;
    }

    public async Task<IReadOnlyList<ParticipantSummaryDto>> GetAllAsync(Guid userId, CancellationToken ct = default)
    {
        // Scoped to the caller's programs (#1) — a teacher listing participants must not
        // see (or be able to enumerate the ids of) children in programs they don't teach.
        var access = await _access.ForUserAsync(userId);

        var participants = (await _uow.Participants.GetAllAsync(ct))
            .Where(p => access.CanAccess(p.ProgramId))
            .ToList();
        if (participants.Count == 0) return new List<ParticipantSummaryDto>();

        var programs = await _uow.Programs.GetAllAsync(ct);
        var programMap = programs.ToDictionary(p => p.Id, p => p.Name);
        var slugMap = programs.ToDictionary(p => p.Id, p => p.Slug);

        // Attendance % from SQL-side aggregates (#8/#11) — no whole-ledger load.
        var pctMap = AttendanceStats.PercentByParticipant(await _stats.GetParticipantAttendanceAsync(ct));
        return participants.Select(p => ToSummary(p, programMap, slugMap, pctMap)).ToList();
    }

    public async Task<ParticipantDetailDto?> GetByIdAsync(Guid userId, Guid id)
    {
        var p = await _access.RequireParticipantAsync(userId, id);
        if (p is null) return null;

        var prog = await _uow.Programs.GetByIdAsync(p.ProgramId);
        var records = await _uow.Attendance.ListAsync(r => r.ParticipantId == id);

        return new ParticipantDetailDto
        {
            Id = p.Id,
            FullName = p.FullName,
            Initials = p.Initials,
            Status = p.Status,
            ProgramId = p.ProgramId,
            ProgramName = prog?.Name ?? string.Empty,
            ProgramSlug = prog?.Slug ?? string.Empty,
            AttendancePct = AttendanceStats.PercentFor(records),
            StartDate = p.StartDate.ToString("yyyy-MM-dd"),
            HasDocAlerts = false,
            BirthYear = p.BirthYear,
            ServiceCoordinator = p.ServiceCoordinator,
            Documents = new(),
            RecentAttendance = new(),
        };
    }

    public async Task<ParticipantDetailDto> CreateAsync(Guid userId, CreateParticipantDto dto)
    {
        // You may only enrol a child into a program you run.
        var access = await _access.ForUserAsync(userId);
        access.Require(dto.ProgramId);

        var participant = new Participant
        {
            FullName = dto.FullName,
            Initials = dto.Initials,
            ProgramId = dto.ProgramId,
            Status = dto.Status,
            BirthYear = dto.BirthYear,
            ServiceCoordinator = dto.ServiceCoordinator,
            StartDate = dto.StartDate ?? DateTime.UtcNow,
        };

        await _uow.Participants.AddAsync(participant);
        await _uow.SaveChangesAsync();

        return (await GetByIdAsync(userId, participant.Id))!;
    }

    public async Task<ParticipantDetailDto?> UpdateAsync(Guid userId, Guid id, UpdateParticipantDto dto)
    {
        var participant = await _access.RequireParticipantAsync(userId, id);
        if (participant is null) return null;

        // Moving a child between programs needs scope over the destination too, otherwise
        // it becomes a way to push records into a program you can't otherwise write to.
        if (dto.ProgramId.HasValue && dto.ProgramId.Value != participant.ProgramId)
        {
            var access = await _access.ForUserAsync(userId);
            access.Require(dto.ProgramId.Value);
        }

        if (dto.FullName is not null) participant.FullName = dto.FullName;
        if (dto.Initials is not null) participant.Initials = dto.Initials;
        if (dto.ProgramId.HasValue) participant.ProgramId = dto.ProgramId.Value;
        if (dto.Status.HasValue) participant.Status = dto.Status.Value;
        if (dto.BirthYear.HasValue) participant.BirthYear = dto.BirthYear;
        if (dto.ServiceCoordinator is not null) participant.ServiceCoordinator = dto.ServiceCoordinator;

        await _uow.Participants.UpdateAsync(participant);
        await _uow.SaveChangesAsync();

        return await GetByIdAsync(userId, id);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id)
    {
        var participant = await _access.RequireParticipantAsync(userId, id);
        if (participant is null) return false;

        await _uow.Participants.DeleteAsync(participant);
        await _uow.SaveChangesAsync();
        return true;
    }

    private static ParticipantSummaryDto ToSummary(
        Participant p,
        Dictionary<Guid, string> programMap,
        Dictionary<Guid, string>? slugMap = null,
        Dictionary<Guid, int>? pctMap = null) =>
        new()
        {
            Id = p.Id,
            FullName = p.FullName,
            Initials = p.Initials,
            Status = p.Status,
            ProgramId = p.ProgramId,
            ProgramName = programMap.GetValueOrDefault(p.ProgramId, string.Empty),
            ProgramSlug = slugMap?.GetValueOrDefault(p.ProgramId, string.Empty) ?? string.Empty,
            AttendancePct = pctMap?.GetValueOrDefault(p.Id, 0) ?? p.AttendancePct,
            StartDate = p.StartDate.ToString("yyyy-MM-dd"),
            HasDocAlerts = false,
            BirthYear = p.BirthYear,
            ServiceCoordinator = p.ServiceCoordinator,
        };
}
