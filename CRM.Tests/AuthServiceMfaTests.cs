using CRM.Application.Auth;
using CRM.Application.DTOs.Auth;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace CRM.Tests;

/// <summary>
/// The two-step login state machine.
///
/// The real TotpService and MfaSecretProtector are used rather than fakes — both are pure,
/// and a fake validator would let a broken implementation pass. Only the repository layer and
/// the password hasher are stood in for (no database, and PBKDF2's 100k iterations are
/// covered by PasswordHasherTests rather than repeated a hundred times here).
/// </summary>
public class AuthServiceMfaTests
{
    private const string GoodPassword = "Sup3rSecret!";
    private const string WrongPassword = "Wr0ngGuess!";

    private static readonly Guid EnrolledId = Guid.Parse("11111111-0000-0000-0000-0000000000e1");
    private static readonly Guid PlainId = Guid.Parse("22222222-0000-0000-0000-0000000000e2");
    private static readonly Guid InactiveId = Guid.Parse("33333333-0000-0000-0000-0000000000e3");

    /// <summary>A second admin, so a reset of somebody else is visibly not a self-reset.</summary>
    private static readonly Guid OtherAdminId = Guid.Parse("44444444-0000-0000-0000-0000000000e4");

    private const string EnrolledEmail = "enrolled@example.org";
    private const string PlainEmail = "plain@example.org";

    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeAuditService _audit = new();
    private readonly TotpService _totp = TestMfa.Totp();
    private readonly MfaSecretProtector _protector = TestMfa.Protector();
    private readonly AuthService _service;

    /// <summary>The enrolled user's TOTP secret in the clear, so tests can act as the phone.</summary>
    private readonly byte[] _secret;

    public AuthServiceMfaTests()
    {
        var hasher = new FakePasswordHasher();
        var (hash, salt) = hasher.HashPassword(GoodPassword);

        _secret = _totp.GenerateSecret();

        _uow.UsersRepo.Items.AddRange([
            new User
            {
                Id = EnrolledId, Email = EnrolledEmail, FullName = "Enrolled",
                Role = UserRole.Staff, IsActive = true, PasswordHash = hash, PasswordSalt = salt,
                MfaEnabled = true, MfaSecret = _protector.Protect(EnrolledId, _secret),
                MfaEnabledAt = DateTime.UtcNow.AddDays(-1),
            },
            new User
            {
                Id = PlainId, Email = PlainEmail, FullName = "Plain",
                Role = UserRole.Admin, IsActive = true, PasswordHash = hash, PasswordSalt = salt,
            },
            new User
            {
                Id = InactiveId, Email = "gone@example.org", FullName = "Gone",
                Role = UserRole.Staff, IsActive = false, PasswordHash = hash, PasswordSalt = salt,
                MfaEnabled = true, MfaSecret = _protector.Protect(InactiveId, _secret),
            },
        ]);

        _service = new AuthService(
            _uow, hasher, new FakeTokenService(), _totp, _protector,
            _audit, NullLogger<AuthService>.Instance);
    }

    // ---------- helpers ----------

    private Task<LoginOutcome> LoginAsync(string email, string password = GoodPassword) =>
        _service.LoginAsync(new LoginDto { Email = email, Password = password });

    private string CurrentCode() => TestMfa.CodeAt(_secret, DateTimeOffset.UtcNow);

    /// <summary>
    /// A six-digit code that is valid for none of the three steps in the acceptance window.
    /// Picking one by tweaking a digit would flake roughly one run in half a million, which is
    /// exactly often enough to waste somebody's afternoon a year from now.
    /// </summary>
    private static string InvalidCodeFor(byte[] secret)
    {
        var step = TotpService.ComputeStep(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var valid = new[] { step - 1, step, step + 1 }
            .Select(s => TotpService.ComputeCode(secret, s, 6))
            .ToHashSet();

        for (var i = 0; i < 10; i++)
        {
            var candidate = i.ToString("D6");
            if (!valid.Contains(candidate)) return candidate;
        }

        throw new InvalidOperationException("Unreachable: only three codes can be valid.");
    }

    private User Enrolled => _uow.UsersRepo.Items.Single(u => u.Id == EnrolledId);
    private User Plain => _uow.UsersRepo.Items.Single(u => u.Id == PlainId);

    // ---------- the password step ----------

    [Fact]
    public async Task Enrolled_user_with_the_right_password_gets_a_challenge_and_no_session()
    {
        var outcome = await LoginAsync(EnrolledEmail);

        Assert.True(outcome.MfaRequired);
        Assert.Null(outcome.Session);
        Assert.NotNull(outcome.ChallengeToken);

        // No session means no refresh token: the second factor is not skippable by design,
        // not by a check somewhere downstream.
        Assert.Empty(_uow.RefreshTokensRepo.Items);
        Assert.Single(_uow.MfaChallengesRepo.Items);
    }

    [Fact]
    public async Task The_password_step_is_not_a_login()
    {
        await LoginAsync(EnrolledEmail);

        // LastLoginAt would otherwise record an attempt that never completed, which matters:
        // it is what an admin looks at to answer "when did this account last get in?".
        Assert.Null(Enrolled.LastLoginAt);
    }

    [Fact]
    public async Task Unenrolled_user_signs_in_at_the_password_step()
    {
        var outcome = await LoginAsync(PlainEmail);

        Assert.False(outcome.MfaRequired);
        Assert.NotNull(outcome.Session);
        Assert.NotNull(Plain.LastLoginAt);
        Assert.Single(_uow.RefreshTokensRepo.Items);
    }

    [Fact]
    public async Task A_wrong_password_for_an_enrolled_user_creates_no_challenge()
    {
        // The enumeration side-channel. If the enrolled branch ran before the password verdict,
        // the mere existence of a challenge row (or an ss_mfa cookie on the response) would
        // tell an attacker which addresses have accounts — for free, without a valid password.
        var outcome = await LoginAsync(EnrolledEmail, WrongPassword);

        Assert.False(outcome.Authenticated);
        Assert.False(outcome.MfaRequired);
        Assert.Null(outcome.ChallengeToken);
        Assert.Empty(_uow.MfaChallengesRepo.Items);
    }

    [Fact]
    public async Task Unknown_email_wrong_password_and_inactive_account_are_indistinguishable()
    {
        var unknown = await LoginAsync("nobody@example.org");
        var wrong = await LoginAsync(EnrolledEmail, WrongPassword);
        var inactive = await LoginAsync("gone@example.org");

        foreach (var outcome in new[] { unknown, wrong, inactive })
        {
            Assert.Same(LoginOutcome.Failed, outcome);
            Assert.False(outcome.Authenticated);
            Assert.Null(outcome.Session);
            Assert.Null(outcome.ChallengeToken);
        }

        // Not one challenge between them — including for the inactive account, which does have
        // MFA enrolled.
        Assert.Empty(_uow.MfaChallengesRepo.Items);
    }

    [Fact]
    public async Task Issuing_a_challenge_is_audited()
    {
        await LoginAsync(EnrolledEmail);

        var entry = _audit.Single("auth.mfa.challenge");
        Assert.True(entry.Succeeded);
        Assert.Equal(EnrolledId, entry.UserId);

        // Not a completed sign-in, so no auth.login success row yet.
        Assert.Empty(_audit.WithAction("auth.login"));
    }

    [Fact]
    public async Task A_second_login_kills_the_outstanding_challenge()
    {
        // Only one challenge is ever live for an account. Note what this does NOT do: it stops
        // an attacker holding several challenges in PARALLEL, and nothing more. Sequential
        // guessing — five wrong codes, re-post the password, five more — is bounded by the
        // per-account throttle instead (see the lockout tests below).
        var first = await LoginAsync(EnrolledEmail);
        await LoginAsync(EnrolledEmail);

        Assert.Equal(2, _uow.MfaChallengesRepo.Items.Count);
        Assert.Single(_uow.MfaChallengesRepo.Items, c => c.ConsumedAt is null);

        var replayed = await _service.VerifyMfaAsync(first.ChallengeToken, CurrentCode());
        Assert.Null(replayed.Session);
    }

    // ---------- the code step ----------

    [Fact]
    public async Task A_valid_code_completes_the_sign_in()
    {
        var login = await LoginAsync(EnrolledEmail);
        var outcome = await _service.VerifyMfaAsync(login.ChallengeToken, CurrentCode());

        Assert.NotNull(outcome.Session);
        Assert.Single(_uow.RefreshTokensRepo.Items);
        Assert.NotNull(Enrolled.LastLoginAt);
        Assert.NotNull(_uow.MfaChallengesRepo.Items.Single().ConsumedAt);
        Assert.True(Enrolled.LastTotpStep > 0);

        var entry = _audit.Single("auth.login");
        Assert.True(entry.Succeeded);
        Assert.Contains("totp", entry.Metadata);
    }

    [Fact]
    public async Task A_consumed_challenge_cannot_be_reused()
    {
        var login = await LoginAsync(EnrolledEmail);
        await _service.VerifyMfaAsync(login.ChallengeToken, CurrentCode());

        // Same cookie, replayed. Even with a code that would otherwise be valid.
        var again = await _service.VerifyMfaAsync(login.ChallengeToken, CurrentCode());

        Assert.Null(again.Session);
        Assert.False(again.ChallengeSurvives);
        Assert.Contains(_audit.WithAction("auth.mfa.verify"), e => e.Metadata!.Contains("challenge replay"));
    }

    [Fact]
    public async Task An_expired_challenge_fails()
    {
        var login = await LoginAsync(EnrolledEmail);
        _uow.MfaChallengesRepo.Items.Single().ExpiresAt = DateTime.UtcNow.AddMinutes(-1);

        var outcome = await _service.VerifyMfaAsync(login.ChallengeToken, CurrentCode());

        Assert.Null(outcome.Session);
        Assert.False(outcome.ChallengeSurvives);
    }

    [Fact]
    public async Task An_unknown_or_missing_challenge_token_fails()
    {
        Assert.Null((await _service.VerifyMfaAsync(null, "123456")).Session);
        Assert.Null((await _service.VerifyMfaAsync("", "123456")).Session);
        Assert.Null((await _service.VerifyMfaAsync("not-a-real-token", "123456")).Session);
    }

    [Fact]
    public async Task Five_wrong_codes_kill_the_challenge_even_if_the_sixth_is_right()
    {
        var login = await LoginAsync(EnrolledEmail);
        var wrong = InvalidCodeFor(_secret);

        for (var attempt = 1; attempt <= MfaChallenge.MaxAttempts; attempt++)
        {
            var failed = await _service.VerifyMfaAsync(login.ChallengeToken, wrong);
            Assert.Null(failed.Session);
            // The challenge survives while guesses remain, so a typo does not eject the user
            // back to the password screen — but the fifth one is fatal.
            Assert.Equal(attempt < MfaChallenge.MaxAttempts, failed.ChallengeSurvives);
        }

        var correct = await _service.VerifyMfaAsync(login.ChallengeToken, CurrentCode());

        Assert.Null(correct.Session);
        Assert.Empty(_uow.RefreshTokensRepo.Items);
        Assert.Contains(_audit.WithAction("auth.mfa.verify"), e => e.Metadata!.Contains("challenge exhausted"));
    }

    // ---------- the per-account throttle ----------
    //
    // MfaChallenge.MaxAttempts caps guesses against ONE challenge, and on its own it caps
    // nothing: a fresh challenge starts at zero attempts, so an attacker already holding the
    // password loops login -> five wrong codes -> login indefinitely, for one extra HTTP
    // request per five guesses. Six digits do not survive that. These tests pin the counter
    // that survives a new challenge.

    /// <summary>Burns wrong codes, re-running the password step whenever a challenge dies.</summary>
    private async Task<int> GuessWronglyAsync(int times)
    {
        var wrong = InvalidCodeFor(_secret);
        var challengesIssued = 0;
        LoginOutcome login = await LoginAsync(EnrolledEmail);
        if (login.MfaRequired) challengesIssued++;

        for (var i = 0; i < times; i++)
        {
            if (login.ChallengeToken is null) break;

            var failed = await _service.VerifyMfaAsync(login.ChallengeToken, wrong);
            if (failed.ChallengeSurvives) continue;

            login = await LoginAsync(EnrolledEmail);
            if (login.MfaRequired) challengesIssued++;
        }

        return challengesIssued;
    }

    [Fact]
    public async Task Repeated_wrong_codes_lock_the_account_across_fresh_challenges()
    {
        // Ten failures needs three challenges at five attempts each, so this only passes if
        // the counter outlives the challenge it was counted against.
        await GuessWronglyAsync(10);

        Assert.NotNull(Enrolled.MfaLockedUntil);
        Assert.True(Enrolled.MfaLockedUntil > DateTime.UtcNow);
    }

    [Fact]
    public async Task A_locked_account_gets_the_same_answer_as_a_wrong_password()
    {
        await GuessWronglyAsync(10);

        var challengesBefore = _uow.MfaChallengesRepo.Items.Count;
        var locked = await LoginAsync(EnrolledEmail);
        var wrongPassword = await LoginAsync(EnrolledEmail, WrongPassword);

        // Byte-identical outcomes: no session, no challenge, nothing to distinguish "this
        // account is locked" from "that password is wrong". A locked account that answered
        // differently would be an oracle for anyone, holding a password or not.
        Assert.False(locked.Authenticated);
        Assert.False(locked.MfaRequired);
        Assert.Null(locked.ChallengeToken);
        Assert.Equal(wrongPassword.Authenticated, locked.Authenticated);
        Assert.Equal(wrongPassword.MfaRequired, locked.MfaRequired);

        // And no new challenge row was minted, so the attacker gains no fresh attempt budget.
        Assert.Equal(challengesBefore, _uow.MfaChallengesRepo.Items.Count);
    }

    [Fact]
    public async Task The_lockout_is_audited_and_the_refused_login_records_its_reason()
    {
        await GuessWronglyAsync(10);
        await LoginAsync(EnrolledEmail);

        var lockout = _audit.Single("auth.mfa.lockout");
        Assert.False(lockout.Succeeded);
        Assert.Equal(EnrolledId, lockout.UserId);

        Assert.Contains(
            _audit.WithAction("auth.login"),
            e => !e.Succeeded && e.Metadata!.Contains("second factor locked out"));
    }

    [Fact]
    public async Task A_correct_code_clears_the_failure_count()
    {
        // Nine failures — one short of the lock — then a real sign-in. A user who fumbles a
        // few codes and then succeeds must not be one typo away from a lockout tomorrow.
        await GuessWronglyAsync(9);
        Assert.True(Enrolled.FailedMfaAttempts > 0);

        var login = await LoginAsync(EnrolledEmail);
        var outcome = await _service.VerifyMfaAsync(login.ChallengeToken, CurrentCode());

        Assert.NotNull(outcome.Session);
        Assert.Equal(0, Enrolled.FailedMfaAttempts);
        Assert.Null(Enrolled.LastFailedMfaAt);
        Assert.Null(Enrolled.MfaLockedUntil);
    }

    [Fact]
    public async Task Failures_older_than_the_window_do_not_accumulate()
    {
        // A user who mistypes once a month must never be locked out by arithmetic.
        await GuessWronglyAsync(5);
        Enrolled.LastFailedMfaAt = DateTime.UtcNow.AddHours(-2);

        await GuessWronglyAsync(4);

        Assert.Null(Enrolled.MfaLockedUntil);
    }

    [Fact]
    public async Task An_admin_reset_clears_the_lockout()
    {
        // The reset exists to get a locked-out user back in; leaving the cooldown running
        // would defeat the point of it.
        await GuessWronglyAsync(10);
        Assert.NotNull(Enrolled.MfaLockedUntil);

        await _service.AdminResetMfaAsync(EnrolledId, OtherAdminId);

        Assert.Null(Enrolled.MfaLockedUntil);
        Assert.Equal(0, Enrolled.FailedMfaAttempts);
    }

    [Fact]
    public async Task A_superseded_challenge_is_not_audited_as_a_replay()
    {
        // A teacher starts signing in on the staff-room laptop, finishes on their phone, then
        // goes back to the laptop. Refused either way — that part must not change — but the
        // log is read by a human, and "challenge replay" reads as a captured cookie.
        var laptop = await LoginAsync(EnrolledEmail);
        await LoginAsync(EnrolledEmail);

        var outcome = await _service.VerifyMfaAsync(laptop.ChallengeToken, CurrentCode());

        Assert.Null(outcome.Session);
        Assert.False(outcome.ChallengeSurvives);

        var entry = _audit.WithAction("auth.mfa.verify").Single();
        Assert.Contains("challenge superseded", entry.Metadata);
        Assert.DoesNotContain("challenge replay", entry.Metadata);
    }

    [Fact]
    public async Task A_genuinely_replayed_challenge_is_still_audited_as_a_replay()
    {
        var login = await LoginAsync(EnrolledEmail);
        await _service.VerifyMfaAsync(login.ChallengeToken, CurrentCode());

        await _service.VerifyMfaAsync(login.ChallengeToken, CurrentCode());

        Assert.Contains(
            _audit.WithAction("auth.mfa.verify"),
            e => e.Metadata!.Contains("challenge replay"));
    }

    [Fact]
    public async Task A_rejected_code_is_audited_without_recording_the_code()
    {
        var login = await LoginAsync(EnrolledEmail);
        var wrong = InvalidCodeFor(_secret);

        await _service.VerifyMfaAsync(login.ChallengeToken, wrong);

        var entry = _audit.WithAction("auth.mfa.verify").Single();
        Assert.False(entry.Succeeded);

        // A wrong guess is one digit away from a right one, so a log of near-misses is a log
        // of near-credentials. Only the free-text fields are checked: a six-digit string can
        // appear by coincidence inside a Guid, and asserting over the whole serialized row
        // would fail on that rather than on anything real.
        Assert.DoesNotContain(wrong, entry.Metadata);
        Assert.DoesNotContain(wrong, entry.Summary);
    }

    [Fact]
    public async Task A_code_already_spent_is_refused_on_a_fresh_challenge()
    {
        // RFC 6238 §5.2 end to end: sign in, then try the same still-in-window code again.
        // This is the shoulder-surfing case — someone who read the code off the screen has
        // roughly thirty seconds unless the step is burned.
        var first = await LoginAsync(EnrolledEmail);
        var code = CurrentCode();
        Assert.NotNull((await _service.VerifyMfaAsync(first.ChallengeToken, code)).Session);

        _uow.RefreshTokensRepo.Items.Clear();

        var second = await LoginAsync(EnrolledEmail);
        var replay = await _service.VerifyMfaAsync(second.ChallengeToken, code);

        Assert.Null(replay.Session);
        Assert.Empty(_uow.RefreshTokensRepo.Items);
        Assert.Contains(_audit.WithAction("auth.mfa.verify"), e => e.Metadata!.Contains("totp replayed"));
    }

    [Fact]
    public async Task An_undecryptable_secret_fails_the_code_rather_than_the_request()
    {
        var login = await LoginAsync(EnrolledEmail);

        // What a rotated or lost Mfa:EncryptionKey looks like from inside the request. It must
        // read as a wrong code, not a 500: on an anonymous endpoint a 500 is a distinguishable
        // response, and it would tell a caller something about the state of the stored row.
        Enrolled.MfaSecret = _protector.Protect(Guid.NewGuid(), _secret);

        var outcome = await _service.VerifyMfaAsync(login.ChallengeToken, CurrentCode());

        Assert.Null(outcome.Session);
        Assert.True(outcome.ChallengeSurvives);
    }

    [Fact]
    public async Task A_challenge_survives_an_admin_reset_only_as_a_dead_one()
    {
        var login = await LoginAsync(EnrolledEmail);
        await _service.AdminResetMfaAsync(EnrolledId, OtherAdminId);

        // The user is mid-challenge when their enrollment is wiped. Nothing they type can be
        // right, so the challenge dies rather than letting them burn attempts.
        var outcome = await _service.VerifyMfaAsync(login.ChallengeToken, "123456");

        Assert.Null(outcome.Session);
        Assert.False(outcome.ChallengeSurvives);
    }

    // ---------- recovery codes ----------

    [Fact]
    public async Task A_recovery_code_works_once_and_only_once()
    {
        var codes = await EnrollPlainUserAsync();

        var first = await LoginAsync(PlainEmail);
        var used = await _service.VerifyMfaAsync(first.ChallengeToken, codes[0]);
        Assert.NotNull(used.Session);

        var entry = _audit.WithAction("auth.login").Last();
        Assert.Contains("recovery", entry.Metadata);

        var second = await LoginAsync(PlainEmail);
        var reused = await _service.VerifyMfaAsync(second.ChallengeToken, codes[0]);
        Assert.Null(reused.Session);

        // The other nine are untouched.
        var third = await LoginAsync(PlainEmail);
        Assert.NotNull((await _service.VerifyMfaAsync(third.ChallengeToken, codes[1])).Session);
    }

    [Fact]
    public async Task Recovery_codes_are_never_shaped_like_a_totp_code()
    {
        // How VerifyMfaAsync tells the two apart. If a recovery code could be six digits the
        // discriminator would be ambiguous and a recovery code would be tried as a TOTP.
        var codes = await EnrollPlainUserAsync();

        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Distinct().Count());
        foreach (var code in codes)
        {
            Assert.Matches("^[A-Z2-7]{4}-[A-Z2-7]{4}-[A-Z2-7]{4}-[A-Z2-7]{4}$", code);
            Assert.False(code.Replace("-", "").All(char.IsAsciiDigit));
        }
    }

    [Fact]
    public async Task Recovery_codes_are_matched_regardless_of_how_they_are_typed()
    {
        var codes = await EnrollPlainUserAsync();
        var messy = codes[0].Replace("-", " ").ToLowerInvariant();

        var login = await LoginAsync(PlainEmail);
        Assert.NotNull((await _service.VerifyMfaAsync(login.ChallengeToken, messy)).Session);
    }

    [Fact]
    public async Task Regenerating_replaces_every_code()
    {
        var original = await EnrollPlainUserAsync();
        var replacement = await _service.RegenerateRecoveryCodesAsync(PlainId, NextCodeFor(PlainId));

        Assert.NotNull(replacement);
        Assert.Equal(10, replacement!.Count);
        Assert.Empty(original.Intersect(replacement));
        Assert.Equal(10, _uow.MfaRecoveryCodesRepo.Items.Count);

        // An old code is now worthless — that is the point of regenerating.
        var login = await LoginAsync(PlainEmail);
        Assert.Null((await _service.VerifyMfaAsync(login.ChallengeToken, original[0])).Session);
    }

    [Fact]
    public async Task Status_reports_the_remaining_recovery_codes()
    {
        var codes = await EnrollPlainUserAsync();

        var before = await _service.GetMfaStatusAsync(PlainId);
        Assert.True(before!.Enabled);
        Assert.NotNull(before.EnabledAt);
        Assert.Equal(10, before.RecoveryCodesRemaining);

        var login = await LoginAsync(PlainEmail);
        await _service.VerifyMfaAsync(login.ChallengeToken, codes[0]);

        var after = await _service.GetMfaStatusAsync(PlainId);
        Assert.Equal(9, after!.RecoveryCodesRemaining);
    }

    // ---------- enrollment ----------

    [Fact]
    public async Task Setup_on_an_already_enrolled_account_is_refused()
    {
        // A stray second call would otherwise replace a working secret with one nobody has
        // scanned, locking the user out of their own account at the next sign-in.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SetupMfaAsync(EnrolledId));
    }

    [Fact]
    public async Task Setup_stores_the_secret_encrypted_and_leaves_mfa_off()
    {
        var setup = await _service.SetupMfaAsync(PlainId);

        Assert.NotNull(setup);
        Assert.False(Plain.MfaEnabled);
        Assert.NotNull(Plain.MfaSecret);
        // Stored ciphertext, not the base32 the user was shown.
        Assert.NotEqual(setup!.Secret, Plain.MfaSecret);
        Assert.StartsWith("otpauth://totp/", setup.OtpAuthUri);

        // Not enabled yet, so the password step still completes the sign-in.
        Assert.NotNull((await LoginAsync(PlainEmail)).Session);
    }

    [Fact]
    public async Task Enable_requires_a_valid_code()
    {
        var setup = await _service.SetupMfaAsync(PlainId);
        Assert.True(Base32.TryDecode(setup!.Secret, out var secret));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.EnableMfaAsync(PlainId, InvalidCodeFor(secret)));

        Assert.False(Plain.MfaEnabled);
        Assert.Contains(_audit.WithAction("auth.mfa.enroll"), e => !e.Succeeded);
    }

    [Fact]
    public async Task Enable_revokes_every_pre_enrollment_session()
    {
        // A refresh token minted before enrollment belongs to a session that never presented a
        // second factor. Left alive, a copy stolen beforehand would silently upgrade into a
        // full mfa:true session the moment the user enrolled.
        var priorSession = await LoginAsync(PlainEmail);
        Assert.NotNull(priorSession.Session);
        var priorHash = _uow.RefreshTokensRepo.Items.Single().TokenHash;

        await EnrollPlainUserAsync();

        var prior = _uow.RefreshTokensRepo.Items.Single(t => t.TokenHash == priorHash);
        Assert.NotNull(prior.RevokedAt);

        // The enrolling browser is handed a live replacement, so it is not signed out.
        Assert.Single(_uow.RefreshTokensRepo.Items, t => t.RevokedAt is null);

        // And the revoked one is genuinely dead.
        Assert.Null(await _service.RefreshAsync(priorSession.Session!.RefreshToken));
    }

    [Fact]
    public async Task Enable_burns_the_confirming_code()
    {
        // Otherwise the code just typed into the enrollment form can be turned straight around
        // at /login/mfa inside the same thirty-second window.
        var (_, confirmingCode) = await EnrollPlainUserCoreAsync();

        var login = await LoginAsync(PlainEmail);
        Assert.Null((await _service.VerifyMfaAsync(login.ChallengeToken, confirmingCode)).Session);
    }

    [Fact]
    public async Task Enrollment_is_audited_without_the_secret_or_the_codes()
    {
        var codes = await EnrollPlainUserAsync();

        var entry = _audit.WithAction("auth.mfa.enroll").Single(e => e.Succeeded);
        Assert.Equal(PlainId, entry.UserId);

        var everything = System.Text.Json.JsonSerializer.Serialize(_audit.Entries);
        Assert.DoesNotContain(Plain.MfaSecret!, everything);
        foreach (var code in codes)
            Assert.DoesNotContain(code, everything);
    }

    // ---------- disable and admin reset ----------

    [Fact]
    public async Task Disable_with_a_wrong_password_changes_nothing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DisableMfaAsync(EnrolledId, WrongPassword, CurrentCode()));

        Assert.True(Enrolled.MfaEnabled);
        Assert.NotNull(Enrolled.MfaSecret);
        Assert.Contains(_audit.WithAction("auth.mfa.disable"), e => !e.Succeeded);
    }

    [Fact]
    public async Task Disable_with_a_wrong_code_changes_nothing()
    {
        // Password alone is not enough: a stolen session plus a leaked password would otherwise
        // strip the second factor, which is the one thing it exists to prevent.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DisableMfaAsync(EnrolledId, GoodPassword, InvalidCodeFor(_secret)));

        Assert.True(Enrolled.MfaEnabled);
    }

    [Fact]
    public async Task Disable_clears_everything_and_re_mints_the_session()
    {
        await EnrollPlainUserAsync();
        _uow.RefreshTokensRepo.Items.Clear();
        _uow.RefreshTokensRepo.Items.Add(new RefreshToken
        {
            UserId = PlainId, TokenHash = "old", ExpiresAt = DateTime.UtcNow.AddDays(1),
        });

        var session = await _service.DisableMfaAsync(PlainId, GoodPassword, NextCodeFor(PlainId));

        Assert.NotNull(session);
        Assert.False(Plain.MfaEnabled);
        Assert.Null(Plain.MfaSecret);
        Assert.Equal(0, Plain.LastTotpStep);
        Assert.Empty(_uow.MfaRecoveryCodesRepo.Items);
        Assert.NotNull(_uow.RefreshTokensRepo.Items.Single(t => t.TokenHash == "old").RevokedAt);
    }

    [Fact]
    public async Task Admin_reset_clears_the_target_and_reveals_nothing()
    {
        _uow.RefreshTokensRepo.Items.Add(new RefreshToken
        {
            UserId = EnrolledId, TokenHash = "victim-session", ExpiresAt = DateTime.UtcNow.AddDays(1),
        });
        _uow.MfaRecoveryCodesRepo.Items.Add(new MfaRecoveryCode { UserId = EnrolledId, CodeHash = "x" });
        var storedSecret = Enrolled.MfaSecret!;

        var ok = await _service.AdminResetMfaAsync(EnrolledId, OtherAdminId);

        Assert.True(ok);
        Assert.False(Enrolled.MfaEnabled);
        Assert.Null(Enrolled.MfaSecret);
        Assert.Null(Enrolled.MfaEnabledAt);
        Assert.Equal(0, Enrolled.LastTotpStep);
        Assert.Empty(_uow.MfaRecoveryCodesRepo.Items);

        // The reset exists because the second factor is presumed to be in someone else's
        // hands, so an existing session must not survive it either.
        Assert.NotNull(_uow.RefreshTokensRepo.Items.Single().RevokedAt);

        var entry = _audit.Single("auth.mfa.reset.admin");
        Assert.True(entry.Succeeded);
        // Actor is left null so the audit service fills in the admin, not the target.
        Assert.Null(entry.UserId);
        Assert.Equal(EnrolledId, entry.EntityId);
        Assert.DoesNotContain(storedSecret, System.Text.Json.JsonSerializer.Serialize(entry));
    }

    [Fact]
    public async Task Admin_reset_of_an_unknown_user_reports_not_found() =>
        Assert.False(await _service.AdminResetMfaAsync(Guid.NewGuid(), OtherAdminId));

    // ---------- self-reset ----------
    //
    // Found by driving the running API, not by reading it: an admin session alone used to
    // clear that same account's second factor. DisableMfaAsync demands password AND code so a
    // stolen session cannot do exactly this; the reset endpoint took neither, so a stolen
    // cookie walked around that guard and left the account on password-only.

    [Fact]
    public async Task Self_reset_without_the_password_is_refused()
    {
        var admin = _uow.UsersRepo.Items.Single(u => u.Id == PlainId);
        admin.MfaEnabled = true;
        admin.MfaSecret = _protector.Protect(PlainId, _secret);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AdminResetMfaAsync(PlainId, PlainId));

        Assert.True(admin.MfaEnabled);
        Assert.NotNull(admin.MfaSecret);
    }

    [Fact]
    public async Task Self_reset_with_the_wrong_password_is_refused_and_audited()
    {
        var admin = _uow.UsersRepo.Items.Single(u => u.Id == PlainId);
        admin.MfaEnabled = true;
        admin.MfaSecret = _protector.Protect(PlainId, _secret);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AdminResetMfaAsync(PlainId, PlainId, WrongPassword));

        Assert.True(admin.MfaEnabled);
        var entry = _audit.Entries.Last(e => e.Action == "auth.mfa.reset.admin");
        Assert.False(entry.Succeeded);
        Assert.DoesNotContain(GoodPassword, System.Text.Json.JsonSerializer.Serialize(entry));
    }

    [Fact]
    public async Task Self_reset_with_the_password_succeeds_so_a_lone_admin_is_not_stranded()
    {
        // The reason this path stays open at all: DisableMfaAsync validates TOTP only, so an
        // admin holding recovery codes but no authenticator has no other way back.
        var admin = _uow.UsersRepo.Items.Single(u => u.Id == PlainId);
        admin.MfaEnabled = true;
        admin.MfaSecret = _protector.Protect(PlainId, _secret);

        Assert.True(await _service.AdminResetMfaAsync(PlainId, PlainId, GoodPassword));

        Assert.False(admin.MfaEnabled);
        Assert.Null(admin.MfaSecret);
    }

    [Fact]
    public async Task Resetting_someone_else_still_needs_no_password()
    {
        // The lost-phone case: the admin cannot know the target's password, and requiring one
        // would make the endpoint useless for the only job it has.
        Assert.True(await _service.AdminResetMfaAsync(EnrolledId, OtherAdminId));
        Assert.False(Enrolled.MfaEnabled);
    }

    // ---------- test helpers ----------

    /// <summary>Runs the real setup + enable flow for the plain user and returns their codes.</summary>
    private async Task<IReadOnlyList<string>> EnrollPlainUserAsync()
    {
        var (codes, _) = await EnrollPlainUserCoreAsync();
        return codes;
    }

    private async Task<(IReadOnlyList<string> codes, string confirmingCode)> EnrollPlainUserCoreAsync()
    {
        var setup = await _service.SetupMfaAsync(PlainId);
        Assert.True(Base32.TryDecode(setup!.Secret, out var secret));

        var code = TestMfa.CodeAt(secret, DateTimeOffset.UtcNow);
        var outcome = await _service.EnableMfaAsync(PlainId, code);

        Assert.NotNull(outcome);
        return (outcome!.RecoveryCodes, code);
    }

    /// <summary>
    /// The code this user's authenticator will show in the NEXT step. Enrollment burns the
    /// step of the code that confirmed it, so anything needing a second valid code straight
    /// afterwards has to look forward — which is still inside the +1 skew window.
    /// </summary>
    private string NextCodeFor(Guid userId)
    {
        var user = _uow.UsersRepo.Items.Single(u => u.Id == userId);
        var secret = _protector.Unprotect(userId, user.MfaSecret!);
        var step = TotpService.ComputeStep(DateTimeOffset.UtcNow.ToUnixTimeSeconds()) + 1;
        return TotpService.ComputeCode(secret, step, 6);
    }
}
