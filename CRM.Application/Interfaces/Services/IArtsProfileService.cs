using CRM.Application.DTOs.Participants;

namespace CRM.Application.Interfaces.Services;

/// <summary>
/// The Student Frame (IPP summary, current level, TSSP arts goal) — personalised notes
/// about a child, scoped to the caller's programs (#1).
/// </summary>
public interface IArtsProfileService
{
    /// <summary>The participant's Student Frame, or an empty default (HasProfile=false) if none set. Null if the participant doesn't exist.</summary>
    Task<ParticipantArtsProfileDto?> GetAsync(Guid currentUserId, Guid participantId);

    /// <summary>Creates or updates the Student Frame. Null if the participant doesn't exist.</summary>
    Task<ParticipantArtsProfileDto?> UpsertAsync(Guid currentUserId, Guid participantId, UpsertArtsProfileDto dto);
}
