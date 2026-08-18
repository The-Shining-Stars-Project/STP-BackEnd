using CRM.Application.DTOs.Audit;
using CRM.Application.DTOs.Auth;
using CRM.Application.Exceptions;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace CRM.Tests;

/// <summary>
/// Auth events are audited inside AuthService rather than by the controller filter, because
/// the controller cannot see what happened — LoginAsync returns null for three different
/// reasons, DeleteUserAsync destroys the email the row needs, and UpdateUserAsync is the only
/// place holding a role both before and after a change. These tests are what stops any of
/// those events from quietly disappearing.
/// </summary>
public class AuthServiceAuditTests
{
    private const string GoodPassword = "Sup3rSecret!";
    private const string WrongPassword = "Wr0ngGuess!";

    private static readonly Guid AdminId = Guid.Parse("11111111-0000-0000-0000-00000000000a");
    private static readonly Guid SecondAdminId = Guid.Parse("11111111-0000-0000-0000-00000000000b");
    private static readonly Guid StaffId = Guid.Parse("22222222-0000-0000-0000-00000000000c");
    private static readonly Guid InactiveId = Guid.Parse("33333333-0000-0000-0000-00000000000d");

    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeAuditService _audit = new();
    private readonly AuthService _service;

    public AuthServiceAuditTests()
    {
        var hasher = new FakePasswordHasher();
        var (hash, salt) = hasher.HashPassword(GoodPassword);

        _uow.UsersRepo.Items.AddRange([
            new User { Id = AdminId, Email = "admin@example.org", FullName = "Admin", Role = UserRole.Admin, IsActive = true, PasswordHash = hash, PasswordSalt = salt },
            new User { Id = SecondAdminId, Email = "admin2@example.org", FullName = "Admin Two", Role = UserRole.Admin, IsActive = true, PasswordHash = hash, PasswordSalt = salt },
            new User { Id = StaffId, Email = "teacher@example.org", FullName = "Teacher", Role = UserRole.Staff, IsActive = true, PasswordHash = hash, PasswordSalt = salt },
            new User { Id = InactiveId, Email = "gone@example.org", FullName = "Gone", Role = UserRole.Staff, IsActive = false, PasswordHash = hash, PasswordSalt = salt },
        ]);

        _service = new AuthService(
            _uow, hasher, new FakeTokenService(), TestMfa.Totp(), TestMfa.Protector(),
            _audit, NullLogger<AuthService>.Instance);
    }

    // ---------- login ----------

    [Fact]
    public async Task Successful_login_is_audited()
    {
        await _service.LoginAsync(new LoginDto { Email = "admin@example.org", Password = GoodPassword });

        var entry = _audit.Single("auth.login");
        Assert.True(entry.Succeeded);
        Assert.Equal(AdminId, entry.UserId);
        Assert.Equal("admin@example.org", entry.UserEmail);
        Assert.Equal("Admin", entry.UserRole);
    }

    [Fact]
    public async Task Failed_login_against_an_unknown_email_records_no_user_id()
    {
        await _service.LoginAsync(new LoginDto { Email = "nobody@example.org", Password = GoodPassword });

        var entry = _audit.Single("auth.login");
        Assert.False(entry.Succeeded);
        Assert.Null(entry.UserId);
        Assert.Equal("nobody@example.org", entry.UserEmail);
        Assert.Contains("no such user", entry.Metadata);
    }

    [Fact]
    public async Task Failed_login_with_a_bad_password_records_the_reason_and_the_account()
    {
        await _service.LoginAsync(new LoginDto { Email = "admin@example.org", Password = WrongPassword });

        var entry = _audit.Single("auth.login");
        Assert.False(entry.Succeeded);
        Assert.Equal(AdminId, entry.UserId);
        Assert.Contains("bad password", entry.Metadata);
    }

    [Fact]
    public async Task Failed_login_against_a_deactivated_account_is_distinguishable_in_the_log()
    {
        await _service.LoginAsync(new LoginDto { Email = "gone@example.org", Password = GoodPassword });

        var entry = _audit.Single("auth.login");
        Assert.False(entry.Succeeded);
        Assert.Contains("account inactive", entry.Metadata);
    }

    [Fact]
    public async Task Failed_login_records_the_submitted_email_normalized_for_filtering()
    {
        // AuthService normalizes before the lookup; the audit row must match, or filtering the
        // log by address misses the rows.
        await _service.LoginAsync(new LoginDto { Email = "  ADMIN@Example.ORG  ", Password = WrongPassword });

        Assert.Equal("admin@example.org", AuditEventFactory
            .Build(_audit.Single("auth.login"), null).UserEmail);
    }

    /// <summary>
    /// The one test that directly enforces "never log passwords". It sweeps every string field
    /// of every entry produced by a failed sign-in rather than checking the field a reviewer
    /// happens to be thinking about.
    /// </summary>
    [Fact]
    public async Task No_audit_field_of_a_failed_login_ever_contains_the_submitted_password()
    {
        const string secret = "TotallyUniqueP4ssword!";

        await _service.LoginAsync(new LoginDto { Email = "admin@example.org", Password = secret });
        await _service.LoginAsync(new LoginDto { Email = "nobody@example.org", Password = secret });

        Assert.NotEmpty(_audit.Entries);
        foreach (var entry in _audit.Entries)
            foreach (var field in AllStrings(entry))
                Assert.DoesNotContain(secret, field, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_audit_field_of_a_password_change_ever_contains_either_password()
    {
        const string current = "CurrentUniqueP4ss!";
        const string next = "NextUniqueP4ssword!";

        var hasher = new FakePasswordHasher();
        var (hash, salt) = hasher.HashPassword(current);
        var user = _uow.UsersRepo.Items.Single(u => u.Id == StaffId);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;

        await _service.ChangePasswordAsync(StaffId, new ChangePasswordDto
        {
            CurrentPassword = current,
            NewPassword = next,
        });

        // And the rejected path, which is the one that audits before throwing.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ChangePasswordAsync(StaffId, new ChangePasswordDto
            {
                CurrentPassword = "DefinitelyWr0ng!",
                NewPassword = next,
            }));

        Assert.NotEmpty(_audit.Entries);
        foreach (var entry in _audit.Entries)
            foreach (var field in AllStrings(entry))
            {
                Assert.DoesNotContain(current, field, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(next, field, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("DefinitelyWr0ng!", field, StringComparison.OrdinalIgnoreCase);
            }
    }

    private static IEnumerable<string> AllStrings(AuditEntry entry) =>
        new[] { entry.Action, entry.EntityType, entry.UserEmail, entry.UserRole, entry.Summary, entry.Metadata }
            .Where(v => v is not null)!;

    // ---------- refresh / logout ----------

    [Fact]
    public async Task Refresh_token_replay_is_audited_with_the_number_of_sessions_killed()
    {
        var login = await _service.LoginAsync(new LoginDto { Email = "admin@example.org", Password = GoodPassword });
        var raw = login.Session!.RefreshToken;

        // First use rotates the token; presenting the same one again is the replay.
        await _service.RefreshAsync(raw);
        await _service.RefreshAsync(raw);

        var entry = _audit.Single("auth.refresh.replay");
        Assert.False(entry.Succeeded);
        Assert.Equal(AdminId, entry.UserId);
        Assert.Equal("admin@example.org", entry.UserEmail);
        Assert.Contains("revokedSessions", entry.Metadata);
    }

    [Fact]
    public async Task Successful_refresh_is_not_audited()
    {
        // It fires roughly hourly per active session; auditing it would bury the events that
        // matter under rows carrying no signal.
        var login = await _service.LoginAsync(new LoginDto { Email = "admin@example.org", Password = GoodPassword });

        await _service.RefreshAsync(login.Session!.RefreshToken);

        Assert.Empty(_audit.WithAction("auth.refresh"));
        Assert.Empty(_audit.WithAction("auth.refresh.replay"));
    }

    [Fact]
    public async Task Logout_is_audited_with_the_actor_taken_from_the_token()
    {
        // The endpoint is [AllowAnonymous], so there may be no principal on the request at all.
        var login = await _service.LoginAsync(new LoginDto { Email = "teacher@example.org", Password = GoodPassword });

        await _service.LogoutAsync(login.Session!.RefreshToken);

        var entry = _audit.Single("auth.logout");
        Assert.True(entry.Succeeded);
        Assert.Equal(StaffId, entry.UserId);
        Assert.Equal("teacher@example.org", entry.UserEmail);
    }

    // ---------- passwords ----------

    [Fact]
    public async Task Password_change_is_audited_on_success()
    {
        await _service.ChangePasswordAsync(StaffId, new ChangePasswordDto
        {
            CurrentPassword = GoodPassword,
            NewPassword = "BrandNewP4ss!",
        });

        var entry = _audit.Single("auth.password.change");
        Assert.True(entry.Succeeded);
        Assert.Equal(StaffId, entry.UserId);
    }

    [Fact]
    public async Task Password_change_is_audited_before_the_wrong_password_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ChangePasswordAsync(StaffId, new ChangePasswordDto
            {
                CurrentPassword = WrongPassword,
                NewPassword = "BrandNewP4ss!",
            }));

        var entry = _audit.Single("auth.password.change");
        Assert.False(entry.Succeeded);
        Assert.Contains("current password incorrect", entry.Metadata);
    }

    [Fact]
    public async Task Admin_password_reset_records_the_target_account_not_just_the_actor()
    {
        await _service.ResetPasswordAsync(StaffId, new ResetPasswordDto { NewPassword = "ResetP4ssword!" });

        var entry = _audit.Single("auth.password.reset.admin");
        Assert.True(entry.Succeeded);
        Assert.Equal(StaffId, entry.EntityId);
        Assert.Equal("User", entry.EntityType);
        Assert.Contains("teacher@example.org", entry.Summary);

        // The actor is left null on purpose so the audit service fills it from the request
        // context — the admin performing the reset, not the account being reset.
        Assert.Null(entry.UserId);
    }

    // ---------- user management ----------

    [Fact]
    public async Task User_creation_is_audited_with_the_new_account_as_the_subject()
    {
        var created = await _service.RegisterAsync(new RegisterUserDto
        {
            Email = "New.User@Example.org",
            FullName = "New User",
            Password = "Fresh1Password!",
            Role = UserRole.Staff,
        });

        var entry = _audit.Single("user.create");
        Assert.True(entry.Succeeded);
        Assert.Equal(created.Id, entry.EntityId);
        Assert.Contains("new.user@example.org", entry.Summary);
        Assert.Null(entry.UserId);   // actor comes from the request context
    }

    [Fact]
    public async Task Role_change_records_both_the_old_and_the_new_role()
    {
        await _service.UpdateUserAsync(StaffId, new UpdateUserDto { Role = UserRole.Admin }, AdminId);

        var update = _audit.Single("user.update");
        Assert.Contains("\"roleBefore\":\"Staff\"", update.Metadata);
        Assert.Contains("\"roleAfter\":\"Admin\"", update.Metadata);

        // Also emitted under its own action so it is one filter click away in the viewer.
        var roleChange = _audit.Single("user.role.change");
        Assert.True(roleChange.Succeeded);
        Assert.Equal(StaffId, roleChange.EntityId);
    }

    [Fact]
    public async Task A_plain_edit_does_not_emit_a_role_change_row()
    {
        await _service.UpdateUserAsync(StaffId, new UpdateUserDto { FullName = "Renamed" }, AdminId);

        Assert.Single(_audit.WithAction("user.update"));
        Assert.Empty(_audit.WithAction("user.role.change"));
        Assert.Empty(_audit.WithAction("user.deactivate"));
    }

    [Fact]
    public async Task Deactivating_a_user_emits_its_own_row()
    {
        await _service.UpdateUserAsync(StaffId, new UpdateUserDto { IsActive = false }, AdminId);

        var entry = _audit.Single("user.deactivate");
        Assert.Equal(StaffId, entry.EntityId);
        Assert.Contains("teacher@example.org", entry.Summary);

        var update = _audit.Single("user.update");
        Assert.Contains("\"isActiveBefore\":true", update.Metadata);
        Assert.Contains("\"isActiveAfter\":false", update.Metadata);
    }

    [Fact]
    public async Task A_blocked_attempt_to_strip_the_last_admin_is_audited_as_a_failure()
    {
        // Self-demotion is refused. Nothing else would record the attempt: the endpoint has no
        // [Audited] attribute and the request 409s.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateUserAsync(AdminId, new UpdateUserDto { Role = UserRole.Staff }, AdminId));

        var entry = _audit.Single("user.update");
        Assert.False(entry.Succeeded);
        Assert.Contains("admin@example.org", entry.Summary);
    }

    [Fact]
    public async Task User_deletion_captures_the_email_before_the_row_is_gone()
    {
        // AuditEvent has no foreign key to User precisely so this row outlives the account.
        // The denormalized email is the only thing left that says who it was.
        await _service.DeleteUserAsync(StaffId, AdminId);

        var entry = _audit.Single("user.delete");
        Assert.True(entry.Succeeded);
        Assert.Equal(StaffId, entry.EntityId);
        Assert.Contains("teacher@example.org", entry.Summary);
        Assert.Contains("teacher@example.org", entry.Metadata);

        // And the user really is gone, so nothing else could have supplied that email.
        Assert.Null(await _uow.UsersRepo.GetByIdAsync(StaffId));
    }

    [Fact]
    public async Task A_blocked_deletion_is_audited_as_a_failure()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteUserAsync(AdminId, AdminId));

        var entry = _audit.Single("user.delete");
        Assert.False(entry.Succeeded);
        Assert.Contains("admin@example.org", entry.Summary);
    }

    [Fact]
    public async Task Deleting_a_missing_user_records_nothing()
    {
        var result = await _service.DeleteUserAsync(Guid.NewGuid(), AdminId);

        Assert.False(result);
        Assert.Empty(_audit.Entries);
    }

    // ---------- failures that never reach the save ----------
    //
    // AuthController carries no [Audited] attribute — deliberately, because AuthService records
    // these events itself — which also means there is no filter-level backstop here. A method
    // that records only after a successful save records nothing at all when the save throws or
    // when it returns 4xx before getting there.

    /// <summary>The concurrency conflict two admins editing one user row really produce.</summary>
    private sealed class FakeConcurrencyException : Exception;

    [Fact]
    public async Task A_failed_save_on_a_user_update_still_records_the_attempt()
    {
        // User carries a RowVersion token, so two admins submitting a role change for the same
        // account is not hypothetical: the second one 409s. Without a row, the log says no
        // privilege change was ever attempted.
        _uow.SaveException = new FakeConcurrencyException();

        await Assert.ThrowsAsync<FakeConcurrencyException>(() =>
            _service.UpdateUserAsync(StaffId, new UpdateUserDto { Role = UserRole.Admin }, AdminId));

        var entry = _audit.Single("user.update");
        Assert.False(entry.Succeeded);
        Assert.Equal(StaffId, entry.EntityId);
        // The exception TYPE, never its message — a DbUpdateException's message can quote the
        // row data that caused it.
        Assert.Contains(nameof(FakeConcurrencyException), entry.Metadata);
    }

    [Fact]
    public async Task A_failed_save_on_a_delete_still_records_the_attempt()
    {
        _uow.SaveException = new FakeConcurrencyException();

        await Assert.ThrowsAsync<FakeConcurrencyException>(() =>
            _service.DeleteUserAsync(StaffId, AdminId));

        var entry = _audit.Single("user.delete");
        Assert.False(entry.Succeeded);
        Assert.Contains("teacher@example.org", entry.Summary);
    }

    [Fact]
    public async Task A_failed_save_on_a_password_reset_still_records_the_attempt()
    {
        _uow.SaveException = new FakeConcurrencyException();

        await Assert.ThrowsAsync<FakeConcurrencyException>(() =>
            _service.ResetPasswordAsync(StaffId, new ResetPasswordDto { NewPassword = "ResetP4ssword!" }));

        var entry = _audit.Single("auth.password.reset.admin");
        Assert.False(entry.Succeeded);
    }

    [Fact]
    public async Task A_failed_save_on_a_registration_still_records_the_attempt()
    {
        _uow.SaveException = new FakeConcurrencyException();

        await Assert.ThrowsAsync<FakeConcurrencyException>(() =>
            _service.RegisterAsync(new RegisterUserDto
            {
                Email = "new@example.org", FullName = "New", Password = "N3wPassword!", Role = UserRole.Staff,
            }));

        var entry = _audit.Single("user.create");
        Assert.False(entry.Succeeded);
    }

    [Fact]
    public async Task A_failed_save_on_a_password_change_still_records_the_attempt()
    {
        _uow.SaveException = new FakeConcurrencyException();

        await Assert.ThrowsAsync<FakeConcurrencyException>(() =>
            _service.ChangePasswordAsync(StaffId, new ChangePasswordDto
            {
                CurrentPassword = GoodPassword, NewPassword = "An0therPassword!",
            }));

        var entry = _audit.Single("auth.password.change");
        Assert.False(entry.Succeeded);
        Assert.Equal(StaffId, entry.UserId);
    }

    [Fact]
    public async Task Creating_a_user_on_an_address_that_already_exists_is_audited()
    {
        // DuplicateEmailException, not the base type: the controller keys the 409 off it,
        // and everything else RegisterAsync rejects is a 400.
        await Assert.ThrowsAsync<DuplicateEmailException>(() =>
            _service.RegisterAsync(new RegisterUserDto
            {
                Email = "TEACHER@example.org", FullName = "Impostor", Password = "N3wPassword!", Role = UserRole.Admin,
            }));

        var entry = _audit.Single("user.create");
        Assert.False(entry.Succeeded);
        Assert.Contains("duplicate email", entry.Metadata);
    }

    [Fact]
    public async Task A_rejected_password_is_audited_without_recording_the_password()
    {
        const string banned = "ChangeMe!123";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RegisterAsync(new RegisterUserDto
            {
                Email = "new@example.org", FullName = "New", Password = banned, Role = UserRole.Staff,
            }));

        var entry = _audit.Single("user.create");
        Assert.False(entry.Succeeded);
        // The RULE that was broken — one of this file's own constant strings — and nothing else.
        Assert.Contains("known default", entry.Metadata);
        Assert.DoesNotContain(banned, entry.Metadata);
        Assert.DoesNotContain(banned, entry.Summary);
    }

    // ---------- a dropped audit write ----------

    [Fact]
    public async Task A_dropped_audit_write_does_not_disturb_the_caller()
    {
        // What this actually verifies: when the audit writer silently drops its entry — which
        // is what a database outage looks like from here, because AuditService catches and logs
        // rather than rethrowing — the business operation still completes normally.
        //
        // It does NOT verify that IAuditService never throws. AuthService awaits every audit
        // call with no try/catch, so a throwing writer would propagate straight out of
        // LoginAsync; a fake that threw would turn this test red, not prove a contract. That
        // contract lives in AuditService (CRM.Persistence), which this project does not
        // reference, so it cannot be pinned from here.
        _audit.FailSilently = true;

        var login = await _service.LoginAsync(new LoginDto { Email = "admin@example.org", Password = GoodPassword });

        Assert.NotNull(login.Session);
        Assert.Empty(_audit.Entries);
    }
}
