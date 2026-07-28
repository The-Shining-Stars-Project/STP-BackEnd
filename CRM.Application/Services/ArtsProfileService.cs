using CRM.Application.DTOs.Participants;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

public class ArtsProfileService : IArtsProfileService
{
    private readonly IUnitOfWork _uow;
    private readonly IProgramAccessService _access;

    public ArtsProfileService(IUnitOfWork uow, IProgramAccessService access)
    {
        _uow = uow;
        _access = access;
    }

    public async Task<ParticipantArtsProfileDto?> GetAsync(Guid currentUserId, Guid participantId)
    {
        var participant = await _access.RequireParticipantAsync(currentUserId, participantId);
        if (participant is null) return null;

        var profile = await _uow.ParticipantArtsProfiles.FirstOrDefaultAsync(p => p.ParticipantId == participantId);
        return profile is null
            ? new ParticipantArtsProfileDto { ParticipantId = participantId, HasProfile = false }
            : ToDto(profile);
    }

    public async Task<ParticipantArtsProfileDto?> UpsertAsync(Guid currentUserId, Guid participantId, UpsertArtsProfileDto dto)
    {
        var participant = await _access.RequireParticipantAsync(currentUserId, participantId);
        if (participant is null) return null;

        var profile = await _uow.ParticipantArtsProfiles.FirstOrDefaultAsync(p => p.ParticipantId == participantId);
        if (profile is null)
        {
            profile = new ParticipantArtsProfile
            {
                ParticipantId = participantId,
                IppSummary = Trim(dto.IppSummary),
                CurrentLevel = Trim(dto.CurrentLevel),
                TsspArtsGoal = Trim(dto.TsspArtsGoal),
            };
            await _uow.ParticipantArtsProfiles.AddAsync(profile);
        }
        else
        {
            profile.IppSummary = Trim(dto.IppSummary);
            profile.CurrentLevel = Trim(dto.CurrentLevel);
            profile.TsspArtsGoal = Trim(dto.TsspArtsGoal);
            await _uow.ParticipantArtsProfiles.UpdateAsync(profile);
        }

        await _uow.SaveChangesAsync();
        return ToDto(profile);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ParticipantArtsProfileDto ToDto(ParticipantArtsProfile p) => new()
    {
        ParticipantId = p.ParticipantId,
        IppSummary = p.IppSummary,
        CurrentLevel = p.CurrentLevel,
        TsspArtsGoal = p.TsspArtsGoal,
        HasProfile = true,
    };
}
