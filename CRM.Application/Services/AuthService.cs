using CRM.Application.Auth;
using CRM.Application.DTOs.Audit;
using CRM.Application.Exceptions;
using CRM.Application.DTOs.Auth;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CRM.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly ITotpService _totp;
    private readonly IMfaSecretProtector _protector;
    private readonly IAuditService _audit;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUnitOfWork uow,
        IPasswordHasher hasher,
        ITokenService tokens,
        ITotpService totp,
        IMfaSecretProtector protector,
        IAuditService audit,
        ILogger<AuthService> logger)
    {
        _uow = uow;
        _hasher = hasher;
        _tokens = tokens;
        _totp = totp;
        _protector = protector;
        _audit = audit;
        _logger = logger;
    }

    // Auth events are recorded here rather than with the [Audited] attribute on
    // AuthController, because the controller cannot see what happened. LoginAsync returns
    // null for three different reasons that a security review needs to tell apart;
    // UpdateUserAsync is the only place holding the role both before and after a change; and
    // by the time DeleteUserAsync returns, the email the audit row needs is gone. No
    // AuthController action carries [Audited], so nothing here produces a duplicate row.
    //
    // Every call is awaited rather than fired and forgotten, so ordering is deterministic.
    // IAuditService is contractually non-throwing, which is what keeps that safe.

    public async Task<LoginOutcome> LoginAsync(LoginDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var user = await _uow.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Run the hash check even when the user is missing to avoid leaking
        // account existence via response timing.
        var hash = user?.PasswordHash ?? string.Empty;
        var salt = user?.PasswordSalt ?? string.Empty;
        var valid = _hasher.VerifyPassword(dto.Password, hash, salt);

        if (user is null || !user.IsActive || !valid)
        {
            // Nothing MFA-related happens on this branch, and that is load-bearing. Minting a
            // challenge row (or setting the ss_mfa cookie) before the password verdict would
            // turn "did a challenge appear?" into an account-existence oracle that costs an
            // attacker nothing to read. Every failure exits here having created no state.
            //
            // Log failed attempts so credential-stuffing shows up in the logs. Do not log
            // the password; the email is fine (it was submitted in cleartext anyway).
            var reason = user is null ? "no such user"
                : !user.IsActive ? "account inactive"
                : "bad password";
            _logger.LogWarning("Failed login for {Email}: {Reason}.", email, reason);

            // The submitted email is the actor here — it is raw, unauthenticated input, and
            // AuditEventFactory.Sanitize strips control characters and truncates it before it
            // reaches the table. UserId stays null when no account matched, so an unknown
            // address cannot be mistaken for a real account's failed attempt.
            //
            // The reason is recorded server-side only; the API's response is identical for
            // all three, so this does not leak account existence to the caller. The password
            // appears in no field, here or anywhere else.
            await _audit.RecordAsync(new AuditEntry
            {
                Action = "auth.login",
                EntityType = "User",
                EntityId = user?.Id,
                UserId = user?.Id,
                UserEmail = email,
                UserRole = user?.Role.ToString(),
                Succeeded = false,
                Summary = "Failed sign-in",
                Metadata = Json(new { reason }),
            });
            return LoginOutcome.Failed;
        }

        // The password is right. From here the two paths diverge in response SHAPE, and they
        // have to: a correct password for an enrolled account cannot look like anything an
        // unknown email could produce. What is preserved is that all three FAILURE outcomes
        // above are byte-identical and cost exactly one PBKDF2 derivation. The residual leak
        // — that somebody already holding a correct password learns whether that account has
        // MFA — is accepted; by then enumeration is moot.
        if (user.MfaEnabled)
        {
            // The second-factor throttle is consulted HERE, at the password step, and the
            // placement is the whole design. Refusing later — at the code step — would answer
            // "is this account locked?" to anyone holding the password, and refusing here with
            // a distinct response would answer it to anyone at all. Returning Failed makes a
            // locked account indistinguishable from a mistyped password: same shape, same cost
            // (the PBKDF2 derivation above already happened), no challenge row, no ss_mfa
            // cookie. The reason is recorded server-side, where a reviewer can see it.
            if (IsMfaLockedOut(user, DateTime.UtcNow))
            {
                _logger.LogWarning(
                    "Login refused for user {UserId}: second factor locked out until {Until}.",
                    user.Id, user.MfaLockedUntil);

                await _audit.RecordAsync(new AuditEntry
                {
                    Action = "auth.login",
                    EntityType = "User",
                    EntityId = user.Id,
                    UserId = user.Id,
                    UserEmail = user.Email,
                    UserRole = user.Role.ToString(),
                    Succeeded = false,
                    Summary = "Failed sign-in",
                    Metadata = Json(new { reason = "second factor locked out" }),
                });
                return LoginOutcome.Failed;
            }

            return await IssueMfaChallengeAsync(user);
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _uow.Users.UpdateAsync(user);

        var session = await CreateSessionAsync(user);
        _logger.LogInformation("User {UserId} logged in.", user.Id);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.login",
            EntityType = "User",
            EntityId = user.Id,
            UserId = user.Id,
            UserEmail = user.Email,
            UserRole = user.Role.ToString(),
            Succeeded = true,
            Summary = "Signed in",
            Metadata = Json(new { mfa = false }),
        });

        return LoginOutcome.ForSession(session);
    }

    public async Task<AuthSessionDto?> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;

        var hash = HashRefreshToken(refreshToken);
        var stored = await _uow.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (stored is null)
        {
            _logger.LogWarning("Refresh rejected: token unknown.");
            return null;
        }

        // Replay (#5): tokens are single-use, so presenting one that was already rotated
        // away means two parties hold it — the legitimate client and whoever copied it.
        // Refusing just this request would leave the thief's other tokens working, so kill
        // every session the user has and force a fresh sign-in.
        if (stored.RevokedAt is not null)
        {
            var killed = await RevokeActiveTokensAsync(stored.UserId);
            await _uow.SaveChangesAsync();
            _logger.LogWarning(
                "Refresh token replay detected for user {UserId}; revoked {Count} active session(s).",
                stored.UserId, killed);

            // The most security-significant event in this file: two parties held the same
            // single-use token. The endpoint is anonymous, so the actor comes from the token's
            // owner rather than from any principal on the request.
            await _audit.RecordAsync(new AuditEntry
            {
                Action = "auth.refresh.replay",
                EntityType = "User",
                EntityId = stored.UserId,
                UserId = stored.UserId,
                UserEmail = await EmailForAsync(stored.UserId),
                Succeeded = false,
                Summary = "Refresh token replay detected; all sessions revoked",
                Metadata = Json(new { revokedSessions = killed }),
            });
            return null;
        }

        if (!stored.IsActive)
        {
            _logger.LogWarning("Refresh rejected for user {UserId}: token expired.", stored.UserId);
            return null;
        }

        var user = await _uow.Users.GetByIdAsync(stored.UserId);
        if (user is null || !user.IsActive)
        {
            _logger.LogWarning("Refresh rejected for user {UserId}: account inactive.", stored.UserId);
            return null;
        }

        // Rotation: this token is single-use. Revoking before reissue means a replayed
        // (stolen) token fails loudly instead of silently minting sessions.
        stored.RevokedAt = DateTime.UtcNow;
        await _uow.RefreshTokens.UpdateAsync(stored);

        // Deliberately NOT refused when MFA is required and this user has not enrolled, and
        // this is not an oversight — do not "fix" it. Refusing here would sign people out
        // mid-enrollment, which is precisely the state Mfa:Required puts a team in on the day
        // it is switched on. The reissued JWT carries mfa:false and MfaEnforcementFilter
        // confines the session to the enrollment endpoints, so an unenrolled refresh buys
        // access to nothing.
        //
        // Nor is there a check that this token came from a session that passed MFA. It cannot
        // not have: refresh tokens are minted only by CreateSessionAsync, and the enrolled
        // login path never reaches it without a verified code. The one exception — a token
        // minted before the user enrolled — is closed at the other end, by EnableMfaAsync
        // revoking every existing token.
        return await CreateSessionAsync(user);
    }

    public async Task LogoutAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var hash = HashRefreshToken(refreshToken);
        var stored = await _uow.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (stored is null || stored.RevokedAt is not null) return;

        stored.RevokedAt = DateTime.UtcNow;
        await _uow.RefreshTokens.UpdateAsync(stored);
        await _uow.SaveChangesAsync();
        _logger.LogInformation("User {UserId} logged out.", stored.UserId);

        // Logout is [AllowAnonymous] — the caller may have no principal at all — so the actor
        // is taken from the token being revoked, not from the request.
        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.logout",
            EntityType = "User",
            EntityId = stored.UserId,
            UserId = stored.UserId,
            UserEmail = await EmailForAsync(stored.UserId),
            Succeeded = true,
            Summary = "Signed out",
        });
    }

    // =====================================================================================
    // Multi-factor authentication
    //
    // The whole point of the two-step flow is that CreateSessionAsync is unreachable from the
    // password step for an enrolled user. Nothing below issues a session without either a
    // verified code or a verified password + code.
    // =====================================================================================

    private const int ChallengeMinutes = 5;
    private const int RecoveryCodeCount = 10;

    /// <summary>
    /// Per-ACCOUNT second-factor throttle. MfaChallenge.MaxAttempts caps guesses against one
    /// challenge; this caps them against one account, across as many challenges as somebody
    /// cares to mint. It is the control that actually bounds TOTP brute force — the "login"
    /// rate limiter partitions on a network address, and an attacker calling this publicly
    /// reachable API directly gets a fresh budget for every address they can source from.
    ///
    /// Ten failures inside fifteen minutes buys a fifteen-minute cooldown. That is two full
    /// exhausted challenges before a real user is inconvenienced, against 10/1,000,000 of the
    /// code space per window for an attacker.
    /// </summary>
    private const int MaxMfaFailures = 10;
    private static readonly TimeSpan MfaFailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MfaLockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Generic failure text. Every challenge failure returns this one, unvarying.</summary>
    private const string ChallengeFailureLogFormat =
        "MFA challenge failed for {UserId}: {Reason}.";

    /// <summary>
    /// Mints a single-use challenge for a user who has just proved their password. No session,
    /// no auth cookies, and LastLoginAt is untouched — a password is not a login here.
    /// </summary>
    private async Task<LoginOutcome> IssueMfaChallengeAsync(User user)
    {
        // Kill any challenge already outstanding for this user, so only one is ever live.
        //
        // This does NOT bound guessing on its own, and an earlier comment here claimed it did.
        // Superseding stops an attacker holding several challenges in PARALLEL; it does nothing
        // about running them one after another, which costs one extra POST /api/auth/login per
        // five guesses. The per-account throttle above (MaxMfaFailures) is what bounds that.
        //
        // SupersededAt is set as well as ConsumedAt: both mean "unusable", but only the second
        // one tells the audit log that the SYSTEM killed this challenge rather than a user
        // spending it, which is how a person with two devices stops being logged as a replay.
        var outstanding = await _uow.MfaChallenges.ListAsync(
            c => c.UserId == user.Id && c.ConsumedAt == null);
        var now = DateTime.UtcNow;
        foreach (var stale in outstanding)
        {
            stale.ConsumedAt = now;
            stale.SupersededAt = now;
            await _uow.MfaChallenges.UpdateAsync(stale);
        }

        var raw = GenerateChallengeToken();
        var challenge = new MfaChallenge
        {
            UserId = user.Id,
            TokenHash = Sha256B64(raw),
            ExpiresAt = now.AddMinutes(ChallengeMinutes),
            AttemptCount = 0,
        };
        await _uow.MfaChallenges.AddAsync(challenge);
        await _uow.SaveChangesAsync();

        _logger.LogInformation("MFA challenge issued for user {UserId}.", user.Id);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.mfa.challenge",
            EntityType = "User",
            EntityId = user.Id,
            UserId = user.Id,
            UserEmail = user.Email,
            UserRole = user.Role.ToString(),
            Succeeded = true,
            Summary = "Password accepted; second factor requested",
            Metadata = Json(new { supersededChallenges = outstanding.Count }),
        });

        return LoginOutcome.ForChallenge(raw, challenge.ExpiresAt);
    }

    public async Task<MfaVerifyOutcome> VerifyMfaAsync(string? challengeToken, string code)
    {
        // Every branch below fails with the SAME response ("Invalid or expired code"). The
        // reasons are recorded server-side because a security review needs them; the caller
        // gets one answer, because telling an attacker which of "wrong code", "expired",
        // "already used" and "out of attempts" applies is free reconnaissance.
        if (string.IsNullOrWhiteSpace(challengeToken))
            return MfaVerifyOutcome.Dead;

        var hash = Sha256B64(challengeToken);
        var challenge = await _uow.MfaChallenges.FirstOrDefaultAsync(c => c.TokenHash == hash);
        if (challenge is null)
        {
            _logger.LogWarning("MFA verification rejected: challenge token unknown.");
            return MfaVerifyOutcome.Dead;
        }

        var now = DateTime.UtcNow;

        if (challenge.ConsumedAt is not null)
        {
            // Refused either way, with the same response — but recorded as two different
            // things, because a human reads this log. A SUPERSEDED challenge is somebody who
            // started signing in on one device and finished on another; a replayed one is a
            // token presented after it was spent, which is what a captured cookie looks like.
            var superseded = challenge.SupersededAt is not null;
            _logger.LogWarning(
                ChallengeFailureLogFormat,
                challenge.UserId,
                superseded ? "challenge superseded by a newer sign-in" : "challenge already consumed");

            await AuditChallengeFailureAsync(
                challenge.UserId,
                superseded ? "challenge superseded" : "challenge replay",
                superseded
                    ? "Superseded MFA challenge token (a newer sign-in replaced it)"
                    : "Reused MFA challenge token");
            return MfaVerifyOutcome.Dead;
        }

        if (challenge.ExpiresAt <= now)
        {
            _logger.LogWarning(ChallengeFailureLogFormat, challenge.UserId, "challenge expired");
            return MfaVerifyOutcome.Dead;
        }

        if (challenge.AttemptCount >= MfaChallenge.MaxAttempts)
        {
            await ConsumeAsync(challenge, now);
            _logger.LogWarning(ChallengeFailureLogFormat, challenge.UserId, "attempt cap reached");
            await AuditChallengeFailureAsync(challenge.UserId, "challenge exhausted", "MFA challenge exhausted");
            return MfaVerifyOutcome.Dead;
        }

        var user = await _uow.Users.GetByIdAsync(challenge.UserId);
        if (user is null || !user.IsActive)
        {
            await ConsumeAsync(challenge, now);
            _logger.LogWarning(ChallengeFailureLogFormat, challenge.UserId, "account missing or inactive");
            return MfaVerifyOutcome.Dead;
        }

        // An admin reset can land while the user is staring at the code screen. Their secret
        // is gone, so nothing they type can be right — kill the challenge rather than let them
        // burn attempts.
        if (!user.MfaEnabled || string.IsNullOrEmpty(user.MfaSecret))
        {
            await ConsumeAsync(challenge, now);
            _logger.LogWarning(ChallengeFailureLogFormat, challenge.UserId, "no enrolled second factor");
            return MfaVerifyOutcome.Dead;
        }

        // The account tripped the per-account throttle while this challenge was in hand. The
        // password step already refuses to mint new challenges, so this only closes the last
        // window: the challenge that was live when the lockout began.
        if (IsMfaLockedOut(user, now))
        {
            await ConsumeAsync(challenge, now);
            _logger.LogWarning(ChallengeFailureLogFormat, challenge.UserId, "second factor locked out");
            await AuditChallengeFailureAsync(
                challenge.UserId, "mfa locked out", "MFA code refused: second factor temporarily locked");
            return MfaVerifyOutcome.Dead;
        }

        var normalized = NormalizeCode(code);
        string method;

        if (IsTotpShaped(normalized))
        {
            var (result, step) = ValidateTotp(user, normalized, now);

            if (result != TotpValidationResult.Accepted)
            {
                var reason = result == TotpValidationResult.Replayed ? "totp replayed" : "totp incorrect";
                return await FailAttemptAsync(challenge, user, reason);
            }

            // Closes the replay window (RFC 6238 §5.2): this step, and every earlier one, is
            // now spent. A code read over the user's shoulder is worthless the moment they
            // use it themselves.
            user.LastTotpStep = step;
            method = "totp";
        }
        else
        {
            var codeHash = Sha256B64(normalized);
            var recovery = await _uow.MfaRecoveryCodes.FirstOrDefaultAsync(
                r => r.UserId == user.Id && r.CodeHash == codeHash && r.UsedAt == null);

            if (recovery is null)
                return await FailAttemptAsync(challenge, user, "recovery code invalid or already used");

            recovery.UsedAt = now;
            await _uow.MfaRecoveryCodes.UpdateAsync(recovery);
            method = "recovery";
        }

        challenge.ConsumedAt = now;
        await _uow.MfaChallenges.UpdateAsync(challenge);

        // A completed second factor clears the throttle. Anything else would let a run of
        // fat-fingered codes lock out a user who has just proved they hold the phone.
        ClearMfaThrottle(user);

        user.LastLoginAt = now;
        await _uow.Users.UpdateAsync(user);

        var session = await CreateSessionAsync(user);
        _logger.LogInformation("User {UserId} completed MFA sign-in via {Method}.", user.Id, method);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.login",
            EntityType = "User",
            EntityId = user.Id,
            UserId = user.Id,
            UserEmail = user.Email,
            UserRole = user.Role.ToString(),
            Succeeded = true,
            Summary = $"Signed in with second factor ({method})",
            Metadata = Json(new { mfa = true, method }),
        });

        return MfaVerifyOutcome.Success(session);
    }

    /// <summary>
    /// Records a wrong code against the challenge. The challenge survives only while attempts
    /// remain, so the caller knows whether to keep the ss_mfa cookie.
    /// </summary>
    private async Task<MfaVerifyOutcome> FailAttemptAsync(MfaChallenge challenge, User user, string reason)
    {
        var now = DateTime.UtcNow;

        challenge.AttemptCount++;
        var survives = challenge.AttemptCount < MfaChallenge.MaxAttempts;
        if (!survives) challenge.ConsumedAt = now;

        await _uow.MfaChallenges.UpdateAsync(challenge);

        // Counted against the ACCOUNT as well as the challenge — see MaxMfaFailures. This is
        // the counter a new challenge cannot reset.
        var lockedOut = await RegisterMfaFailureAsync(user, now);

        await _uow.SaveChangesAsync();

        _logger.LogWarning(
            "MFA code rejected for user {UserId} ({Reason}); attempt {Attempt} of {Max}.",
            user.Id, reason, challenge.AttemptCount, MfaChallenge.MaxAttempts);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.mfa.verify",
            EntityType = "User",
            EntityId = user.Id,
            UserId = user.Id,
            UserEmail = user.Email,
            UserRole = user.Role.ToString(),
            Succeeded = false,
            Summary = "Second factor rejected",
            // The submitted code is never recorded. A wrong guess is one digit away from a
            // right one, and a log full of near-misses is a log full of near-credentials.
            Metadata = Json(new
            {
                reason,
                attempt = challenge.AttemptCount,
                maxAttempts = MfaChallenge.MaxAttempts,
            }),
        });

        if (lockedOut)
        {
            _logger.LogWarning(
                "Second factor locked for user {UserId} after {Max} failures; no new challenge until {Until}.",
                user.Id, MaxMfaFailures, user.MfaLockedUntil);

            await _audit.RecordAsync(new AuditEntry
            {
                Action = "auth.mfa.lockout",
                EntityType = "User",
                EntityId = user.Id,
                UserId = user.Id,
                UserEmail = user.Email,
                UserRole = user.Role.ToString(),
                Succeeded = false,
                Summary = "Second factor locked after repeated incorrect codes",
                Metadata = Json(new
                {
                    failures = MaxMfaFailures,
                    windowMinutes = (int)MfaFailureWindow.TotalMinutes,
                    lockoutMinutes = (int)MfaLockoutDuration.TotalMinutes,
                }),
            });
        }

        if (survives) return MfaVerifyOutcome.Retry;

        // Its own row, because "five wrong codes against one challenge" is a different thing
        // to review than five separate wrong codes: it is somebody working through guesses.
        await AuditChallengeFailureAsync(
            user.Id, "challenge exhausted",
            $"MFA challenge exhausted after {MfaChallenge.MaxAttempts} incorrect codes");

        return MfaVerifyOutcome.Dead;
    }

    /// <summary>
    /// True while the account's second factor is in cooldown. Read at the password step, so a
    /// locked account and a wrong password are the same response.
    /// </summary>
    private static bool IsMfaLockedOut(User user, DateTime now) =>
        user.MfaLockedUntil is { } until && until > now;

    /// <summary>
    /// Counts one rejected code against the account and returns whether that tripped the lock.
    /// Does not save — every caller is already inside a save.
    /// </summary>
    private async Task<bool> RegisterMfaFailureAsync(User user, DateTime now)
    {
        // Failures older than the window are not part of this run of guesses. Without this the
        // counter is cumulative for the life of the account, and a user who mistypes once a
        // month is eventually locked out by arithmetic rather than by an attack.
        if (user.LastFailedMfaAt is not { } last || now - last > MfaFailureWindow)
            user.FailedMfaAttempts = 0;

        user.FailedMfaAttempts++;
        user.LastFailedMfaAt = now;

        var lockedOut = user.FailedMfaAttempts >= MaxMfaFailures;
        if (lockedOut)
        {
            user.MfaLockedUntil = now.Add(MfaLockoutDuration);
            // Zeroed so the cooldown expiring gives a full budget back rather than one attempt
            // before re-locking.
            user.FailedMfaAttempts = 0;
        }

        await _uow.Users.UpdateAsync(user);
        return lockedOut;
    }

    /// <summary>Clears the throttle. Does not save.</summary>
    private static void ClearMfaThrottle(User user)
    {
        user.FailedMfaAttempts = 0;
        user.LastFailedMfaAt = null;
        user.MfaLockedUntil = null;
    }

    private async Task ConsumeAsync(MfaChallenge challenge, DateTime now)
    {
        challenge.ConsumedAt = now;
        await _uow.MfaChallenges.UpdateAsync(challenge);
        await _uow.SaveChangesAsync();
    }

    private async Task AuditChallengeFailureAsync(Guid userId, string reason, string summary) =>
        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.mfa.verify",
            EntityType = "User",
            EntityId = userId,
            UserId = userId,
            UserEmail = await EmailForAsync(userId),
            Succeeded = false,
            Summary = summary,
            Metadata = Json(new { reason }),
        });

    public async Task<MfaSetupResultDto?> SetupMfaAsync(Guid userId)
    {
        var user = await _uow.Users.GetByIdAsync(userId);
        if (user is null) return null;

        // Refuse rather than overwrite. A stray second call to /setup would otherwise replace
        // a working enrollment's secret with one nobody has scanned, locking the user out of
        // their own account on the next sign-in.
        if (user.MfaEnabled)
            throw new InvalidOperationException(
                "Two-factor authentication is already enabled. Reset your authenticator app first.");

        var secret = _totp.GenerateSecret();
        user.MfaSecret = _protector.Protect(user.Id, secret);
        user.MfaEnabled = false;
        await _uow.Users.UpdateAsync(user);
        await _uow.SaveChangesAsync();

        // The event is audited; the secret and the URI are not, here or in any log line.
        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.mfa.setup",
            EntityType = "User",
            EntityId = user.Id,
            UserId = user.Id,
            UserEmail = user.Email,
            UserRole = user.Role.ToString(),
            Succeeded = true,
            Summary = "Started two-factor enrollment",
        });

        return new MfaSetupResultDto
        {
            Secret = _totp.ToBase32(secret),
            OtpAuthUri = _totp.BuildOtpAuthUri(user.Email, secret),
        };
    }

    public async Task<MfaEnableOutcome?> EnableMfaAsync(Guid userId, string code)
    {
        var user = await _uow.Users.GetByIdAsync(userId);
        if (user is null) return null;

        if (user.MfaEnabled)
            throw new InvalidOperationException("Two-factor authentication is already enabled.");
        if (string.IsNullOrEmpty(user.MfaSecret))
            throw new InvalidOperationException("Start setup before enabling two-factor authentication.");

        var now = DateTime.UtcNow;
        var (result, step) = ValidateTotp(user, NormalizeCode(code), now);

        if (result != TotpValidationResult.Accepted)
        {
            await _audit.RecordAsync(new AuditEntry
            {
                Action = "auth.mfa.enroll",
                EntityType = "User",
                EntityId = user.Id,
                UserId = user.Id,
                UserEmail = user.Email,
                UserRole = user.Role.ToString(),
                Succeeded = false,
                Summary = "Two-factor enrollment rejected",
                Metadata = Json(new { reason = result.ToString().ToLowerInvariant() }),
            });
            throw new InvalidOperationException("That code is not valid. Check your authenticator app and try again.");
        }

        user.MfaEnabled = true;
        user.MfaEnabledAt = now;
        // Burn the confirming code immediately, or it could be turned straight around at
        // /login/mfa within the same 30-second window.
        user.LastTotpStep = step;
        await _uow.Users.UpdateAsync(user);

        var codes = await ReplaceRecoveryCodesAsync(user.Id, now);

        // Every refresh token this user holds predates their second factor, which means each
        // one represents a session that never presented one. Left alive, a copy stolen before
        // enrollment would silently upgrade into a full mfa:true session the moment they
        // enrolled. Revoke the lot and hand this browser a fresh pair — the same thing an
        // admin password reset already does.
        var revoked = await RevokeActiveTokensAsync(user.Id);

        var session = await CreateSessionAsync(user);
        _logger.LogInformation(
            "User {UserId} enrolled in MFA; revoked {Count} pre-enrollment session(s).", user.Id, revoked);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.mfa.enroll",
            EntityType = "User",
            EntityId = user.Id,
            UserId = user.Id,
            UserEmail = user.Email,
            UserRole = user.Role.ToString(),
            Succeeded = true,
            Summary = "Enabled two-factor authentication",
            Metadata = Json(new { revokedSessions = revoked, recoveryCodes = codes.Count }),
        });

        return new MfaEnableOutcome { RecoveryCodes = codes, Session = session };
    }

    public async Task<IReadOnlyList<string>?> RegenerateRecoveryCodesAsync(Guid userId, string code)
    {
        var user = await _uow.Users.GetByIdAsync(userId);
        if (user is null) return null;

        if (!user.MfaEnabled || string.IsNullOrEmpty(user.MfaSecret))
            throw new InvalidOperationException("Two-factor authentication is not enabled.");

        var now = DateTime.UtcNow;
        var (result, step) = ValidateTotp(user, NormalizeCode(code), now);

        if (result != TotpValidationResult.Accepted)
            throw new InvalidOperationException("That code is not valid. Check your authenticator app and try again.");

        user.LastTotpStep = step;
        await _uow.Users.UpdateAsync(user);

        var codes = await ReplaceRecoveryCodesAsync(user.Id, now);
        await _uow.SaveChangesAsync();

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.mfa.recovery.regenerate",
            EntityType = "User",
            EntityId = user.Id,
            UserId = user.Id,
            UserEmail = user.Email,
            UserRole = user.Role.ToString(),
            Succeeded = true,
            Summary = "Regenerated recovery codes",
            Metadata = Json(new { recoveryCodes = codes.Count }),
        });

        return codes;
    }

    public async Task<AuthSessionDto?> DisableMfaAsync(Guid userId, string currentPassword, string code)
    {
        var user = await _uow.Users.GetByIdAsync(userId);
        if (user is null) return null;

        if (!user.MfaEnabled || string.IsNullOrEmpty(user.MfaSecret))
            throw new InvalidOperationException("Two-factor authentication is not enabled.");

        // Password AND code. Either one alone would let a stolen session, or a stolen phone,
        // strip the account's second factor unilaterally.
        if (!_hasher.VerifyPassword(currentPassword, user.PasswordHash, user.PasswordSalt))
        {
            await AuditMfaDisableFailureAsync(user, "current password incorrect");
            throw new InvalidOperationException("Current password is incorrect.");
        }

        var now = DateTime.UtcNow;
        var (result, _) = ValidateTotp(user, NormalizeCode(code), now);

        if (result != TotpValidationResult.Accepted)
        {
            await AuditMfaDisableFailureAsync(user, "code invalid");
            throw new InvalidOperationException("That code is not valid. Check your authenticator app and try again.");
        }

        await ClearMfaAsync(user);

        var revoked = await RevokeActiveTokensAsync(user.Id);
        var session = await CreateSessionAsync(user);
        _logger.LogInformation(
            "User {UserId} reset their authenticator; revoked {Count} session(s).", user.Id, revoked);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.mfa.disable",
            EntityType = "User",
            EntityId = user.Id,
            UserId = user.Id,
            UserEmail = user.Email,
            UserRole = user.Role.ToString(),
            Succeeded = true,
            Summary = "Removed two-factor authentication",
            Metadata = Json(new { revokedSessions = revoked }),
        });

        return session;
    }

    public async Task<bool> AdminResetMfaAsync(Guid targetUserId, Guid actingUserId, string? currentPassword = null)
    {
        var user = await _uow.Users.GetByIdAsync(targetUserId);
        if (user is null) return false;

        // Self-reset re-authenticates. Resetting another account is an administrative act on
        // somebody else's credential and an admin session is the right authority for it;
        // resetting your OWN is the same privilege DisableMfaAsync guards with password AND
        // code, so letting a bare session do it here would route around that guard entirely —
        // steal a cookie, strip the factor, and the account is back to password-only. The
        // password is the half of that pair a stolen session does not carry. A code is not
        // demanded too, because the lost-authenticator case is precisely why this path exists.
        if (targetUserId == actingUserId)
        {
            if (string.IsNullOrEmpty(currentPassword)
                || !_hasher.VerifyPassword(currentPassword, user.PasswordHash, user.PasswordSalt))
            {
                await _audit.RecordAsync(new AuditEntry
                {
                    Action = "auth.mfa.reset.admin",
                    EntityType = "User",
                    EntityId = targetUserId,
                    Succeeded = false,
                    Summary = "Refused self-reset of two-factor authentication",
                    Metadata = Json(new
                    {
                        targetEmail = user.Email,
                        reason = string.IsNullOrEmpty(currentPassword)
                            ? "password not supplied"
                            : "password incorrect",
                    }),
                });
                throw new InvalidOperationException(
                    "Resetting your own second factor requires your current password.");
            }
        }

        var now = DateTime.UtcNow;
        await ClearMfaAsync(user);

        // Whoever holds the lost phone must not keep an existing session either — the reset
        // exists because the second factor is presumed to be in somebody else's hands.
        var revoked = await RevokeActiveTokensAsync(targetUserId);

        var outstanding = await _uow.MfaChallenges.ListAsync(
            c => c.UserId == targetUserId && c.ConsumedAt == null);
        foreach (var challenge in outstanding)
        {
            challenge.ConsumedAt = now;
            // Killed by the system, not spent by the user — same distinction as a superseded
            // challenge, and for the same reason: the row this produces must not read as a
            // replay when somebody reviews the log after the reset.
            challenge.SupersededAt = now;
            await _uow.MfaChallenges.UpdateAsync(challenge);
        }

        await _uow.SaveChangesAsync();
        _logger.LogWarning(
            "Admin reset MFA for user {UserId}; revoked {Count} session(s).", targetUserId, revoked);

        // Actor fields left null so the audit service fills them from the request context:
        // the admin doing the reset, not the account being reset.
        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.mfa.reset.admin",
            EntityType = "User",
            EntityId = targetUserId,
            Succeeded = true,
            Summary = $"Admin reset two-factor authentication for {user.Email}",
            Metadata = Json(new { targetEmail = user.Email, revokedSessions = revoked }),
        });

        return true;
    }

    public async Task<MfaStatusDto?> GetMfaStatusAsync(Guid userId)
    {
        var user = await _uow.Users.GetByIdAsync(userId);
        if (user is null) return null;

        var remaining = await _uow.MfaRecoveryCodes.ListAsync(r => r.UserId == userId && r.UsedAt == null);

        return new MfaStatusDto
        {
            Enabled = user.MfaEnabled,
            EnabledAt = user.MfaEnabledAt,
            RecoveryCodesRemaining = remaining.Count,
        };
    }

    private async Task AuditMfaDisableFailureAsync(User user, string reason) =>
        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.mfa.disable",
            EntityType = "User",
            EntityId = user.Id,
            UserId = user.Id,
            UserEmail = user.Email,
            UserRole = user.Role.ToString(),
            Succeeded = false,
            Summary = "Rejected attempt to remove two-factor authentication",
            Metadata = Json(new { reason }),
        });

    /// <summary>
    /// Wipes every trace of the user's second factor. Does not save — callers pair it with
    /// session revocation in one transaction.
    /// </summary>
    private async Task ClearMfaAsync(User user)
    {
        user.MfaEnabled = false;
        user.MfaSecret = null;
        user.MfaEnabledAt = null;
        // Reset so a re-enrollment is not stuck refusing every step below the old counter.
        user.LastTotpStep = 0;
        // Likewise the throttle: an admin reset exists to get a locked-out user back in, so
        // leaving a cooldown running would defeat the point of the reset.
        ClearMfaThrottle(user);
        await _uow.Users.UpdateAsync(user);

        var codes = await _uow.MfaRecoveryCodes.ListAsync(r => r.UserId == user.Id);
        foreach (var existing in codes)
            await _uow.MfaRecoveryCodes.DeleteAsync(existing);
    }

    /// <summary>
    /// Deletes any existing recovery codes and issues a fresh set. Returns the plaintext,
    /// which the caller shows once and then has no way to recover.
    /// </summary>
    private async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(Guid userId, DateTime now)
    {
        var existing = await _uow.MfaRecoveryCodes.ListAsync(r => r.UserId == userId);
        foreach (var old in existing)
            await _uow.MfaRecoveryCodes.DeleteAsync(old);

        var plaintext = new List<string>(RecoveryCodeCount);
        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var code = GenerateRecoveryCode();
            plaintext.Add(code);
            await _uow.MfaRecoveryCodes.AddAsync(new MfaRecoveryCode
            {
                UserId = userId,
                CodeHash = Sha256B64(NormalizeCode(code)),
                CreatedAt = now,
            });
        }

        return plaintext;
    }

    /// <summary>
    /// 80 bits of randomness rendered as base32 and grouped for reading off paper:
    /// XXXX-XXXX-XXXX-XXXX. Sixteen characters, so it can never be mistaken for the six-digit
    /// TOTP code — which is exactly how VerifyMfaAsync decides which kind it was given.
    /// </summary>
    private static string GenerateRecoveryCode()
    {
        var raw = Base32.Encode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(10));
        return string.Join('-', Enumerable.Range(0, 4).Select(i => raw.Substring(i * 4, 4)));
    }

    /// <summary>
    /// Decrypts the user's secret and checks the code. A secret that will not decrypt — the
    /// encryption key was rotated or lost, or the row was tampered with — is reported as a
    /// wrong code rather than allowed to become a 500. On the anonymous /login/mfa endpoint a
    /// 500 would be a distinguishable response, and on the others it would be an unhelpful
    /// one; the ILogger line is where an operator learns the real cause.
    /// </summary>
    private (TotpValidationResult Result, long Step) ValidateTotp(User user, string code, DateTime now)
    {
        try
        {
            var secret = _protector.Unprotect(user.Id, user.MfaSecret!);
            return _totp.Validate(secret, code, user.LastTotpStep, now);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger.LogError(
                ex,
                "Stored MFA secret for user {UserId} could not be decrypted. Check Mfa:EncryptionKey — "
                + "if it changed, every enrolled user needs an admin MFA reset.",
                user.Id);
            return (TotpValidationResult.Rejected, 0);
        }
    }

    /// <summary>Uppercase, separators stripped — so display formatting cannot affect a lookup.</summary>
    private static string NormalizeCode(string? code) =>
        (code ?? string.Empty).Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

    private static bool IsTotpShaped(string normalized) =>
        normalized.Length == 6 && normalized.All(char.IsAsciiDigit);

    private static string GenerateChallengeToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// The denormalized email an audit row needs when the request carries no principal.
    /// One indexed primary-key lookup, on paths that run at most once per session.
    /// </summary>
    private async Task<string?> EmailForAsync(Guid userId) =>
        (await _uow.Users.GetByIdAsync(userId))?.Email;

    private static string Json(object value) => System.Text.Json.JsonSerializer.Serialize(value);

    // =====================================================================================
    // Auditing a persistence failure.
    //
    // Every user-management method below records its row AFTER SaveChangesAsync returns, which
    // is right for the success case and silent for every other one. AuthController carries no
    // [Audited] attribute — deliberately, because this file records these events itself — so
    // unlike every other controller there is no filter-level backstop to catch an in-flight
    // exception. User carries a RowVersion concurrency token, so two admins submitting a role
    // change for the same account is not hypothetical: the second gets a
    // DbUpdateConcurrencyException, GlobalExceptionHandler returns 409, and without this the
    // log contains nothing to say a privilege change was even attempted.
    // =====================================================================================

    /// <summary>
    /// Saves, recording a failed row before letting the exception out. Metadata carries the
    /// exception TYPE only — never the message, which for a DbUpdateException can quote the row
    /// data that caused it, and that data is the thing this log exists to protect.
    /// </summary>
    private async Task SaveOrAuditFailureAsync(string action, Guid? entityId, string summary, User? actor = null)
    {
        try
        {
            await _uow.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            await _audit.RecordAsync(new AuditEntry
            {
                Action = action,
                EntityType = "User",
                EntityId = entityId,
                UserId = actor?.Id,
                UserEmail = actor?.Email,
                UserRole = actor?.Role.ToString(),
                Succeeded = false,
                Summary = summary,
                Metadata = Json(new { exception = ex.GetType().Name }),
            });
            throw;
        }
    }

    /// <summary>
    /// <see cref="ValidatePassword"/>, but a rejection leaves a row. Repeated attempts to set a
    /// banned password are worth seeing. Only the RULE that was broken is recorded — those are
    /// this file's own constant strings — never the password or any part of it.
    /// </summary>
    private async Task ValidatePasswordAuditedAsync(string password, string action, Guid? entityId, string summary, User? actor = null)
    {
        try
        {
            ValidatePassword(password);
        }
        catch (InvalidOperationException ex)
        {
            await _audit.RecordAsync(new AuditEntry
            {
                Action = action,
                EntityType = "User",
                EntityId = entityId,
                UserId = actor?.Id,
                UserEmail = actor?.Email,
                UserRole = actor?.Role.ToString(),
                Succeeded = false,
                Summary = summary,
                Metadata = Json(new { reason = ex.Message }),
            });
            throw;
        }
    }

    // RefreshAsync SUCCESS is deliberately not audited. It fires roughly hourly for every
    // active session and would bury the events that matter under rows carrying no signal —
    // the sign-in that started the session is already recorded.

    private const int RefreshTokenDays = 14;

    /// <summary>Mints a JWT + fresh refresh token for the user and persists pending changes.</summary>
    private async Task<AuthSessionDto> CreateSessionAsync(User user)
    {
        // Include the linked staff role so management-write policies can allow Coordinators.
        StaffRole? staffRole = user.StaffMemberId is { } sid
            ? (await _uow.Staff.GetByIdAsync(sid))?.Role
            : null;

        var (token, expiresAt) = _tokens.CreateToken(user, staffRole);

        var raw = GenerateRefreshToken();
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashRefreshToken(raw),
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays),
        };
        await _uow.RefreshTokens.AddAsync(refresh);
        await _uow.SaveChangesAsync();

        return new AuthSessionDto
        {
            Auth = new AuthResultDto { Token = token, ExpiresAt = expiresAt, User = ToDto(user) },
            RefreshToken = raw,
            RefreshExpiresAt = refresh.ExpiresAt,
        };
    }

    /// <summary>
    /// Revokes every unrevoked refresh token for a user, optionally sparing one (the caller's
    /// own session). Does not save — the caller decides the transaction boundary.
    /// Returns how many were revoked.
    /// </summary>
    private async Task<int> RevokeActiveTokensAsync(Guid userId, string? exceptTokenHash = null)
    {
        var active = await _uow.RefreshTokens.ListAsync(t => t.UserId == userId && t.RevokedAt == null);
        var now = DateTime.UtcNow;
        var revoked = 0;

        foreach (var token in active)
        {
            if (exceptTokenHash is not null && token.TokenHash == exceptTokenHash) continue;
            token.RevokedAt = now;
            await _uow.RefreshTokens.UpdateAsync(token);
            revoked++;
        }

        return revoked;
    }

    private static string GenerateRefreshToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));

    private static string HashRefreshToken(string raw) => Sha256B64(raw);

    /// <summary>
    /// The storage form for every high-entropy credential in this file: refresh tokens, MFA
    /// challenge tokens, recovery codes. A fast hash is the right choice for all three — they
    /// are values this server generated from a CSPRNG, so there is no low-entropy guess for a
    /// slow KDF to defend against. Passwords go through PBKDF2 instead, for the opposite reason.
    /// </summary>
    private static string Sha256B64(string raw) =>
        Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));

    private const int MinPasswordLength = 8;

    public async Task<UserDto> RegisterAsync(RegisterUserDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        await ValidatePasswordAuditedAsync(
            dto.Password, "user.create", null, $"Rejected new user {email}: password not accepted");

        var existing = await _uow.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existing is not null)
        {
            // A 4xx with no row is how repeated attempts to create an account against an
            // address that already exists become invisible. The email is the interesting part
            // and it is already in the log for the account that owns it.
            await _audit.RecordAsync(new AuditEntry
            {
                Action = "user.create",
                EntityType = "User",
                EntityId = existing.Id,
                Succeeded = false,
                Summary = $"Rejected new user {email}: address already in use",
                Metadata = Json(new { email, reason = "duplicate email" }),
            });
            throw new DuplicateEmailException(email);
        }

        var (hash, salt) = _hasher.HashPassword(dto.Password);

        var user = new User
        {
            Email = email,
            FullName = dto.FullName.Trim(),
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = dto.Role,
            StaffMemberId = dto.StaffMemberId,
            IsActive = true,
        };

        await _uow.Users.AddAsync(user);
        await SaveOrAuditFailureAsync("user.create", user.Id, $"Failed to create user {email}");

        // Actor fields are left null so the audit service fills them from the request
        // context: the admin who created the account, not the account being created.
        await _audit.RecordAsync(new AuditEntry
        {
            Action = "user.create",
            EntityType = "User",
            EntityId = user.Id,
            Succeeded = true,
            Summary = $"Created user {user.Email} with role {user.Role}",
            Metadata = Json(new { email = user.Email, role = user.Role.ToString() }),
        });

        return ToDto(user);
    }

    public async Task<UserDto?> GetByIdAsync(Guid id)
    {
        var user = await _uow.Users.GetByIdAsync(id);
        return user is null ? null : ToDto(user);
    }

    public async Task<IReadOnlyList<UserDto>> GetAllAsync()
    {
        var users = await _uow.Users.GetAllAsync();
        return users.Select(ToDto).ToList();
    }

    public async Task<UserDto?> UpdateUserAsync(Guid id, UpdateUserDto dto, Guid actingUserId)
    {
        var user = await _uow.Users.GetByIdAsync(id);
        if (user is null) return null;

        var resultingRole = dto.Role ?? user.Role;
        var resultingActive = dto.IsActive ?? user.IsActive;

        // Captured before the mutations below: this method is the only place that holds both
        // the old and the new values, which is why role changes cannot be audited by the
        // controller filter.
        var previousRole = user.Role;
        var previousActive = user.IsActive;
        var targetEmail = user.Email;

        // Block changes that would strip the system of its last usable admin.
        var losingAdmin = user.Role == UserRole.Admin && user.IsActive
                          && (resultingRole != UserRole.Admin || !resultingActive);
        if (losingAdmin)
        {
            try
            {
                await GuardLastAdminAsync(id, actingUserId, "remove admin access from");
            }
            catch (InvalidOperationException ex)
            {
                // A blocked attempt to strip the last admin is exactly the kind of thing an
                // audit log exists for, and nothing else would record it — the endpoint has
                // no [Audited] attribute and the request 409s.
                await _audit.RecordAsync(new AuditEntry
                {
                    Action = "user.update",
                    EntityType = "User",
                    EntityId = id,
                    Succeeded = false,
                    Summary = $"Blocked role/active change for {targetEmail}",
                    Metadata = Json(new { reason = ex.Message }),
                });
                throw;
            }
        }

        if (dto.FullName is not null) user.FullName = dto.FullName.Trim();
        if (dto.Role.HasValue) user.Role = dto.Role.Value;
        if (dto.IsActive.HasValue) user.IsActive = dto.IsActive.Value;
        if (dto.StaffMemberId.HasValue) user.StaffMemberId = dto.StaffMemberId;

        await _uow.Users.UpdateAsync(user);
        await SaveOrAuditFailureAsync("user.update", id, $"Failed to update user {targetEmail}");

        var roleChanged = previousRole != user.Role;
        var deactivated = previousActive && !user.IsActive;

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "user.update",
            EntityType = "User",
            EntityId = user.Id,
            Succeeded = true,
            Summary = $"Updated user {user.Email}",
            Metadata = Json(new
            {
                email = user.Email,
                roleBefore = previousRole.ToString(),
                roleAfter = user.Role.ToString(),
                isActiveBefore = previousActive,
                isActiveAfter = user.IsActive,
            }),
        });

        // A privilege change and a deactivation each get their own row as well as the generic
        // update. Duplication is the point: these are the two events somebody reviewing the
        // log will search for by name, and making them one filter click away beats hoping the
        // reviewer thinks to expand every user.update row's metadata.
        if (roleChanged)
        {
            await _audit.RecordAsync(new AuditEntry
            {
                Action = "user.role.change",
                EntityType = "User",
                EntityId = user.Id,
                Succeeded = true,
                Summary = $"Role for {user.Email}: {previousRole} -> {user.Role}",
                Metadata = Json(new { roleBefore = previousRole.ToString(), roleAfter = user.Role.ToString() }),
            });
        }

        if (deactivated)
        {
            await _audit.RecordAsync(new AuditEntry
            {
                Action = "user.deactivate",
                EntityType = "User",
                EntityId = user.Id,
                Succeeded = true,
                Summary = $"Deactivated user {user.Email}",
            });
        }

        return ToDto(user);
    }

    public async Task<bool> ResetPasswordAsync(Guid id, ResetPasswordDto dto)
    {
        var user = await _uow.Users.GetByIdAsync(id);
        if (user is null) return false;

        await ValidatePasswordAuditedAsync(
            dto.NewPassword, "auth.password.reset.admin", id,
            $"Rejected admin password reset for {user.Email}: password not accepted");

        var (hash, salt) = _hasher.HashPassword(dto.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;

        await _uow.Users.UpdateAsync(user);

        // An admin reset is the response to a compromised account, so every existing session
        // dies — including the attacker's (#5). Without this their 14-day refresh token keeps
        // minting JWTs long after the password they stole stopped working.
        var revoked = await RevokeActiveTokensAsync(id);

        await SaveOrAuditFailureAsync(
            "auth.password.reset.admin", id, $"Failed admin password reset for {user.Email}");
        _logger.LogInformation(
            "Password reset for user {UserId}; revoked {Count} active session(s).", id, revoked);

        // Actor and subject differ here: the actor is the admin (filled from the request
        // context), the subject is the account whose password was reset. Recording only one
        // of the two would make this event useless.
        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.password.reset.admin",
            EntityType = "User",
            EntityId = id,
            Succeeded = true,
            Summary = $"Admin reset password for {user.Email}",
            Metadata = Json(new { targetEmail = user.Email, revokedSessions = revoked }),
        });
        return true;
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordDto dto, string? currentRefreshToken = null)
    {
        var user = await _uow.Users.GetByIdAsync(userId);
        if (user is null) return false;

        if (!_hasher.VerifyPassword(dto.CurrentPassword, user.PasswordHash, user.PasswordSalt))
        {
            // Audited before the throw, or the exception escapes and the attempt goes
            // unrecorded. Repeated failures here are somebody working on a session they
            // should not have. Neither password appears in any field.
            await _audit.RecordAsync(new AuditEntry
            {
                Action = "auth.password.change",
                EntityType = "User",
                EntityId = user.Id,
                UserId = user.Id,
                UserEmail = user.Email,
                UserRole = user.Role.ToString(),
                Succeeded = false,
                Summary = "Password change rejected",
                Metadata = Json(new { reason = "current password incorrect" }),
            });
            throw new InvalidOperationException("Current password is incorrect.");
        }

        await ValidatePasswordAuditedAsync(
            dto.NewPassword, "auth.password.change", user.Id, "Password change rejected", user);

        var (hash, salt) = _hasher.HashPassword(dto.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;

        await _uow.Users.UpdateAsync(user);

        // Signing out everywhere else is the point of changing a password (#5). The tab the
        // change was made from keeps working — its token is spared when the caller supplies
        // it, which is what every mainstream app does and avoids logging people out of the
        // page they are looking at.
        var keep = string.IsNullOrWhiteSpace(currentRefreshToken)
            ? null
            : HashRefreshToken(currentRefreshToken);
        var revoked = await RevokeActiveTokensAsync(userId, keep);

        await SaveOrAuditFailureAsync(
            "auth.password.change", user.Id, "Password change failed", user);
        _logger.LogInformation(
            "User {UserId} changed their password; revoked {Count} other session(s).", userId, revoked);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "auth.password.change",
            EntityType = "User",
            EntityId = user.Id,
            UserId = user.Id,
            UserEmail = user.Email,
            UserRole = user.Role.ToString(),
            Succeeded = true,
            Summary = "Changed own password",
            Metadata = Json(new { revokedSessions = revoked }),
        });
        return true;
    }

    public async Task<bool> DeleteUserAsync(Guid id, Guid actingUserId)
    {
        var user = await _uow.Users.GetByIdAsync(id);
        if (user is null) return false;

        // Captured before the delete. After the save this row is gone, and since AuditEvent
        // deliberately has no foreign key to User, the denormalized email in the audit row is
        // the only remaining record that this account ever existed.
        var deletedEmail = user.Email;
        var deletedRole = user.Role.ToString();

        if (user.Role == UserRole.Admin && user.IsActive)
        {
            try
            {
                await GuardLastAdminAsync(id, actingUserId, "delete");
            }
            catch (InvalidOperationException ex)
            {
                await _audit.RecordAsync(new AuditEntry
                {
                    Action = "user.delete",
                    EntityType = "User",
                    EntityId = id,
                    Succeeded = false,
                    Summary = $"Blocked deletion of {deletedEmail}",
                    Metadata = Json(new { reason = ex.Message }),
                });
                throw;
            }
        }

        await _uow.Users.DeleteAsync(user);
        await SaveOrAuditFailureAsync("user.delete", id, $"Failed to delete user {deletedEmail}");

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "user.delete",
            EntityType = "User",
            EntityId = id,
            Succeeded = true,
            Summary = $"Deleted user {deletedEmail}",
            Metadata = Json(new { email = deletedEmail, role = deletedRole }),
        });
        return true;
    }

    /// <summary>
    /// Prevents an admin from locking everyone out: you cannot demote/disable/delete
    /// your own account, nor the final remaining active admin.
    /// </summary>
    private async Task GuardLastAdminAsync(Guid targetId, Guid actingUserId, string verb)
    {
        if (targetId == actingUserId)
            throw new InvalidOperationException($"You cannot {verb} your own account.");

        var activeAdmins = await _uow.Users.ListAsync(u => u.Role == UserRole.Admin && u.IsActive);
        if (activeAdmins.Count <= 1)
            throw new InvalidOperationException($"Cannot {verb} the last active admin.");
    }

    // Known-compromised defaults that must never be (re)set: the original seeder password
    // was committed to a public repo, so it is permanently burned (#21).
    private static readonly string[] BlockedPasswords = ["ChangeMe!123"];

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw new InvalidOperationException($"Password must be at least {MinPasswordLength} characters.");

        if (BlockedPasswords.Any(p => string.Equals(p, password, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("That password is a known default and cannot be used. Choose a different one.");

        var hasLetter = password.Any(char.IsLetter);
        var hasDigit = password.Any(char.IsDigit);
        if (!hasLetter || !hasDigit)
            throw new InvalidOperationException("Password must contain at least one letter and one number.");
    }

    private static UserDto ToDto(User u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        FullName = u.FullName,
        Role = u.Role,
        IsActive = u.IsActive,
        StaffMemberId = u.StaffMemberId,
        MfaEnabled = u.MfaEnabled,
    };
}
