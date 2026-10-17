using CRM.Application.DTOs.Auth;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace CRM.Tests;

/// <summary>
/// Oct 2026 (Scott Davis): a login is scoped by the ONE staff record it links to, so a link to a
/// former record, or to a duplicate another login already holds, silently shows the teacher
/// nothing. The backend refuses those links when they are made or changed, and records every
/// link change in the user.update audit row.
/// </summary>
public class UserStaffLinkTests
{
    private const string Password = "Sup3rSecret!";

    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeAuditService _audit = new();
    private readonly AuthService _service;
    private readonly User _admin;
    private readonly User _teacher;
    private readonly StaffMember _current = new() { FullName = "Scott Davis", Initials = "SD", Role = StaffRole.Teacher };
    private readonly StaffMember _duplicate = new()
    {
        FullName = "Do not use duplicate Scott Davis", Initials = "SD", Role = StaffRole.Teacher,
        EndDate = new DateTime(2026, 9, 17),
    };

    public UserStaffLinkTests()
    {
        var hasher = new FakePasswordHasher();
        var (hash, salt) = hasher.HashPassword(Password);
        _admin = new User { Email = "admin@example.org", FullName = "Admin", Role = UserRole.Admin, IsActive = true, PasswordHash = hash, PasswordSalt = salt };
        _teacher = new User { Email = "sdavis@example.org", FullName = "Scott Davis", Role = UserRole.Staff, IsActive = true, PasswordHash = hash, PasswordSalt = salt };
        _uow.UsersRepo.Items.AddRange([_admin, _teacher]);
        _uow.StaffRepo.Items.AddRange([_current, _duplicate]);
        _service = new AuthService(
            _uow, hasher, new FakeTokenService(), TestMfa.Totp(), TestMfa.Protector(),
            _audit, NullLogger<AuthService>.Instance);
    }

    private RegisterUserDto NewLogin(Guid? staffMemberId) => new()
    {
        Email = "new.teacher@example.org", FullName = "New Teacher", Password = Password,
        Role = UserRole.Staff, StaffMemberId = staffMemberId,
    };

    [Fact]
    public async Task Linking_a_new_login_to_an_active_free_record_works()
    {
        var created = await _service.RegisterAsync(NewLogin(_current.Id));

        Assert.Equal(_current.Id, created.StaffMemberId);
    }

    [Fact]
    public async Task Linking_to_a_former_record_is_rejected_on_create_and_update()
    {
        var onCreate = await Assert.ThrowsAsync<ArgumentException>(() => _service.RegisterAsync(NewLogin(_duplicate.Id)));
        Assert.Contains("former", onCreate.Message);
        Assert.Contains("Onboarding", onCreate.Message);
        Assert.DoesNotContain(_uow.UsersRepo.Items, u => u.Email == "new.teacher@example.org");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UpdateUserAsync(_teacher.Id, new UpdateUserDto { StaffMemberId = _duplicate.Id }, _admin.Id));
        Assert.Null(_teacher.StaffMemberId);
    }

    [Fact]
    public async Task Linking_to_a_record_that_does_not_exist_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UpdateUserAsync(_teacher.Id, new UpdateUserDto { StaffMemberId = Guid.NewGuid() }, _admin.Id));

        Assert.Equal("That staff record doesn't exist.", ex.Message);
        Assert.Null(_teacher.StaffMemberId);
    }

    [Fact]
    public async Task Linking_to_a_record_another_login_holds_is_rejected_and_names_that_login()
    {
        _teacher.StaffMemberId = _current.Id;

        var onCreate = await Assert.ThrowsAsync<ArgumentException>(() => _service.RegisterAsync(NewLogin(_current.Id)));
        Assert.Contains("sdavis@example.org", onCreate.Message);

        var other = new User { Email = "scott.typo@example.org", FullName = "Scott D", Role = UserRole.Staff, IsActive = true };
        _uow.UsersRepo.Items.Add(other);
        var onUpdate = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UpdateUserAsync(other.Id, new UpdateUserDto { StaffMemberId = _current.Id }, _admin.Id));
        Assert.Contains("sdavis@example.org", onUpdate.Message);
        Assert.Null(other.StaffMemberId);
    }

    [Fact]
    public async Task Resaving_a_user_with_their_existing_link_still_works_even_if_it_went_former()
    {
        // Linked first, marked former later: an unrelated edit must not start failing.
        _teacher.StaffMemberId = _duplicate.Id;

        var updated = await _service.UpdateUserAsync(
            _teacher.Id, new UpdateUserDto { FullName = "Scott J. Davis", StaffMemberId = _duplicate.Id }, _admin.Id);

        Assert.Equal("Scott J. Davis", updated!.FullName);
        Assert.Equal(_duplicate.Id, _teacher.StaffMemberId);
    }

    [Fact]
    public async Task Relinking_the_same_user_to_their_own_active_record_is_not_a_conflict()
    {
        _teacher.StaffMemberId = _current.Id;

        var updated = await _service.UpdateUserAsync(_teacher.Id, new UpdateUserDto { StaffMemberId = _current.Id }, _admin.Id);

        Assert.Equal(_current.Id, updated!.StaffMemberId);
    }

    [Fact]
    public async Task Clearing_a_link_is_always_allowed()
    {
        _teacher.StaffMemberId = _duplicate.Id;

        var updated = await _service.UpdateUserAsync(_teacher.Id, new UpdateUserDto { ClearStaffMember = true }, _admin.Id);

        Assert.Null(updated!.StaffMemberId);
        Assert.Null(_teacher.StaffMemberId);
    }

    [Fact]
    public async Task Moving_a_login_off_the_duplicate_records_the_link_before_and_after()
    {
        _teacher.StaffMemberId = _duplicate.Id;

        await _service.UpdateUserAsync(_teacher.Id, new UpdateUserDto { StaffMemberId = _current.Id }, _admin.Id);

        var entry = _audit.Single("user.update");
        Assert.True(entry.Succeeded);
        Assert.Contains($"\"staffMemberIdBefore\":\"{_duplicate.Id}\"", entry.Metadata);
        Assert.Contains($"\"staffMemberIdAfter\":\"{_current.Id}\"", entry.Metadata);
        Assert.Contains("\"staffNameBefore\":\"Do not use duplicate Scott Davis\"", entry.Metadata);
        Assert.Contains("\"staffNameAfter\":\"Scott Davis\"", entry.Metadata);
    }

    [Fact]
    public async Task A_refused_link_leaves_a_failed_audit_row()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.UpdateUserAsync(_teacher.Id, new UpdateUserDto { StaffMemberId = _duplicate.Id }, _admin.Id));

        var entry = _audit.Single("user.update");
        Assert.False(entry.Succeeded);
        Assert.Equal(_teacher.Id, entry.EntityId);
        Assert.Contains(_duplicate.Id.ToString(), entry.Metadata);
        Assert.Contains("former", entry.Metadata);
    }
}
