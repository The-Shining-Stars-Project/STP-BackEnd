using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Application.Services;

/// <inheritdoc cref="IProgramAccessService"/>
public class ProgramAccessService : IProgramAccessService
{
    private readonly IUnitOfWork _uow;

    public ProgramAccessService(IUnitOfWork uow) => _uow = uow;

    public async Task<ProgramAccess> ForUserAsync(Guid userId)
    {
        var user = await _uow.Users.GetByIdAsync(userId);

        // Unknown or deactivated account: no scope. Deactivation is also enforced at the
        // token layer, but a scope of "nothing" is the safe answer either way.
        if (user is null || !user.IsActive) return ProgramAccess.None;

        if (user.Role == UserRole.Admin) return new ProgramAccess(true, new HashSet<Guid>());

        // A non-admin with no staff link has no programs — fail closed rather than
        // falling through to an unfiltered view.
        if (user.StaffMemberId is not { } staffId) return ProgramAccess.None;

        var assignments = await _uow.GetStaffProgramAssignmentsAsync();
        return new ProgramAccess(false, assignments
            .Where(a => a.StaffMemberId == staffId)
            .Select(a => a.ProgramId)
            .ToHashSet());
    }

    public async Task<Participant?> RequireParticipantAsync(Guid userId, Guid participantId)
    {
        var participant = await _uow.Participants.GetByIdAsync(participantId);
        if (participant is null) return null;

        var access = await ForUserAsync(userId);
        access.Require(participant.ProgramId);
        return participant;
    }
}
