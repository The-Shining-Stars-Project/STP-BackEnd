using CRM.Application.DTOs.Participants;

namespace CRM.Application.Interfaces.Services;

/// <summary>
/// Participant records are children's PII, so every method is scoped to the caller's
/// programs (#1). Admins see everything; staff see only the programs they're assigned to.
/// </summary>
public interface IParticipantService
{
    /// <summary>Participants in the caller's programs. Out-of-scope rows are filtered out, not rejected.</summary>
    Task<IReadOnlyList<ParticipantSummaryDto>> GetAllAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Null if the participant doesn't exist; throws <see cref="UnauthorizedAccessException"/> if out of scope.</summary>
    Task<ParticipantDetailDto?> GetByIdAsync(Guid userId, Guid id);

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> if the caller isn't assigned to the target program.</summary>
    Task<ParticipantDetailDto> CreateAsync(Guid userId, CreateParticipantDto dto);

    /// <summary>Requires scope over both the participant's current program and, when moving them, the destination.</summary>
    Task<ParticipantDetailDto?> UpdateAsync(Guid userId, Guid id, UpdateParticipantDto dto);

    Task<bool> DeleteAsync(Guid userId, Guid id);
}
