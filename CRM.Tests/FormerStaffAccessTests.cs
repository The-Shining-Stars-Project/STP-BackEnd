using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Xunit;

namespace CRM.Tests;

/// <summary>Marking a staff member former ends their program access; a secondary enrolment counts for scope.</summary>
public class FormerStaffAccessTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly ProgramAccessService _access;
    private static readonly Guid ProgramA = Guid.NewGuid();
    private static readonly Guid ProgramB = Guid.NewGuid();

    public FormerStaffAccessTests() => _access = new ProgramAccessService(_uow);

    private async Task<Guid> Teacher(Guid program, DateTime? endDate = null)
    {
        var staff = new StaffMember { FullName = "T", Initials = "T", EndDate = endDate };
        await _uow.Staff.AddAsync(staff);
        var user = new User { Email = $"{Guid.NewGuid():N}@x.org", Role = UserRole.Staff, IsActive = true, StaffMemberId = staff.Id };
        await _uow.Users.AddAsync(user);
        _uow.StaffProgramAssignments.Add(new StaffProgramAssignment { StaffMemberId = staff.Id, ProgramId = program });
        return user.Id;
    }

    [Fact]
    public async Task Former_staff_have_no_program_scope()
    {
        var active = await Teacher(ProgramA);
        var former = await Teacher(ProgramA, endDate: new DateTime(2026, 8, 31));

        Assert.True((await _access.ForUserAsync(active)).CanAccess(ProgramA));
        Assert.False((await _access.ForUserAsync(former)).CanAccess(ProgramA));
    }

    [Fact]
    public async Task Secondary_enrollment_grants_access_to_the_star()
    {
        var teacherB = await Teacher(ProgramB);
        var star = new Participant { FullName = "S", Initials = "S", ProgramId = ProgramA, SecondaryProgramId = ProgramB };
        await _uow.Participants.AddAsync(star);

        Assert.NotNull(await _access.RequireParticipantAsync(teacherB, star.Id));
    }

    [Fact]
    public async Task Unrelated_program_is_still_refused()
    {
        var teacherB = await Teacher(ProgramB);
        var star = new Participant { FullName = "S", Initials = "S", ProgramId = ProgramA };
        await _uow.Participants.AddAsync(star);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _access.RequireParticipantAsync(teacherB, star.Id));
    }
}
