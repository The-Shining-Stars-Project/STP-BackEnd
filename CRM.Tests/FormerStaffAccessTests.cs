using System.IdentityModel.Tokens.Jwt;
using CRM.Application.DTOs.Auth;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CRM.Tests;

/// <summary>
/// Marking a staff member former ends their program access and their staff role; a secondary
/// enrolment counts for scope.
/// </summary>
public class FormerStaffAccessTests
{
    private const string Password = "Sup3rSecret!";

    private readonly FakeUnitOfWork _uow = new();
    private readonly ProgramAccessService _access;
    private readonly AuthService _auth;
    private static readonly Guid ProgramA = Guid.NewGuid();
    private static readonly Guid ProgramB = Guid.NewGuid();

    public FormerStaffAccessTests()
    {
        _access = new ProgramAccessService(_uow);
        // The real TokenService, so the tests read the claim the ManagementWrite policy reads.
        var tokens = new TokenService(Options.Create(new JwtSettings { Key = "test-only-signing-key-that-is-at-least-32-bytes-long" }));
        _auth = new AuthService(
            _uow, new FakePasswordHasher(), tokens, TestMfa.Totp(), TestMfa.Protector(),
            new FakeAuditService(), NullLogger<AuthService>.Instance);
    }

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

    // ---------- staff role claim (Oct 2026) ----------
    // ManagementWrite trusts the staffRole claim. Before Oct 2026 it was minted from the linked
    // record's role with no EndDate check, so a former Coordinator kept management rights.

    private async Task<(User User, StaffMember Staff)> Coordinator(DateTime? endDate = null)
    {
        var staff = new StaffMember { FullName = "C", Initials = "C", Role = StaffRole.Coordinator, EndDate = endDate };
        await _uow.Staff.AddAsync(staff);
        var (hash, salt) = new FakePasswordHasher().HashPassword(Password);
        var user = new User
        {
            Email = $"{Guid.NewGuid():N}@x.org", FullName = "C", Role = UserRole.Staff, IsActive = true,
            StaffMemberId = staff.Id, PasswordHash = hash, PasswordSalt = salt,
        };
        await _uow.Users.AddAsync(user);
        return (user, staff);
    }

    private static string? StaffRoleClaim(AuthSessionDto session) =>
        new JwtSecurityTokenHandler().ReadJwtToken(session.Auth.Token)
            .Claims.FirstOrDefault(c => c.Type == TokenService.StaffRoleClaim)?.Value;

    private async Task<AuthSessionDto> SignIn(User user) =>
        (await _auth.LoginAsync(new LoginDto { Email = user.Email, Password = Password })).Session!;

    [Fact]
    public async Task Former_coordinator_gets_no_staff_role_claim_and_is_not_management()
    {
        var (active, _) = await Coordinator();
        var (former, _) = await Coordinator(endDate: new DateTime(2026, 8, 31));

        var activeSession = await SignIn(active);
        Assert.Equal("Coordinator", StaffRoleClaim(activeSession));
        Assert.Equal(StaffRole.Coordinator, activeSession.Auth.User.StaffRole);

        var formerSession = await SignIn(former);
        Assert.Null(StaffRoleClaim(formerSession));
        Assert.Null(formerSession.Auth.User.StaffRole);

        // /me must agree with the token, or the frontend's canManage shows buttons that 403.
        Assert.Equal(StaffRole.Coordinator, (await _auth.GetByIdAsync(active.Id))!.StaffRole);
        Assert.Null((await _auth.GetByIdAsync(former.Id))!.StaffRole);
    }

    [Fact]
    public async Task Coordinator_marked_former_mid_session_loses_the_claim_at_the_next_refresh()
    {
        var (user, staff) = await Coordinator();
        var session = await SignIn(user);
        Assert.Equal("Coordinator", StaffRoleClaim(session));

        staff.EndDate = new DateTime(2026, 10, 16);
        var refreshed = await _auth.RefreshAsync(session.RefreshToken);

        Assert.NotNull(refreshed);
        Assert.Null(StaffRoleClaim(refreshed));
    }
}
