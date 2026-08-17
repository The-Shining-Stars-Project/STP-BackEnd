using System.ComponentModel.DataAnnotations;

namespace CRM.Application.DTOs.Auth;

// ---------------------------------------------------------------------------
// Service-internal outcomes. These carry the raw challenge token and the freshly
// minted session, which is why they are outcomes rather than response DTOs: the
// controller turns them into cookies, and none of these values may ever be
// serialized into a response body — the same discipline AuthSessionDto already
// applies to the refresh token.
// ---------------------------------------------------------------------------

/// <summary>
/// What the password step produced. Three states, and the controller needs to tell them
/// apart: a null return value cannot distinguish "wrong password" from "password was right,
/// now show the code screen".
/// </summary>
public sealed class LoginOutcome
{
    private LoginOutcome() { }

    /// <summary>True when the password was correct but a second factor is still owed.</summary>
    public bool MfaRequired { get; private init; }

    /// <summary>Non-null only when the sign-in completed at the password step.</summary>
    public AuthSessionDto? Session { get; private init; }

    /// <summary>Raw challenge token for the ss_mfa cookie. Never goes in a response body.</summary>
    public string? ChallengeToken { get; private init; }

    public DateTime? ChallengeExpiresAt { get; private init; }

    /// <summary>Whether the password step passed at all.</summary>
    public bool Authenticated => Session is not null || MfaRequired;

    /// <summary>
    /// Wrong password, unknown email, or a deactivated account. All three are the same value
    /// on purpose — the controller cannot accidentally respond differently to them.
    /// </summary>
    public static LoginOutcome Failed { get; } = new();

    public static LoginOutcome ForSession(AuthSessionDto session) => new() { Session = session };

    public static LoginOutcome ForChallenge(string token, DateTime expiresAt) =>
        new() { MfaRequired = true, ChallengeToken = token, ChallengeExpiresAt = expiresAt };
}

/// <summary>
/// What a submitted code produced. The distinction that matters is whether the ss_mfa cookie
/// should survive: a wrong code with attempts remaining leaves the user on the code screen,
/// anything else kills the challenge and sends them back to the password step.
/// </summary>
public sealed class MfaVerifyOutcome
{
    private MfaVerifyOutcome() { }

    public AuthSessionDto? Session { get; private init; }

    /// <summary>True only for a wrong code that still has attempts left on a live challenge.</summary>
    public bool ChallengeSurvives { get; private init; }

    public static MfaVerifyOutcome Success(AuthSessionDto session) => new() { Session = session };

    /// <summary>Wrong code, attempts remain. Keep the cookie so the user can try again.</summary>
    public static MfaVerifyOutcome Retry { get; } = new() { ChallengeSurvives = true };

    /// <summary>Challenge is missing, expired, consumed, or exhausted. Clear the cookie.</summary>
    public static MfaVerifyOutcome Dead { get; } = new();
}

/// <summary>
/// Enrollment result. The recovery codes are plaintext and exist in this object for exactly
/// one HTTP response; the session is here because enabling MFA revokes every refresh token
/// the user had, including the enrolling browser's, which then needs a fresh pair.
/// </summary>
public sealed class MfaEnableOutcome
{
    public required IReadOnlyList<string> RecoveryCodes { get; init; }
    public required AuthSessionDto Session { get; init; }
}

// ---------------------------------------------------------------------------
// Request / response DTOs
// ---------------------------------------------------------------------------

/// <summary>
/// What POST /api/auth/login returns now. Auth is null exactly when MfaRequired is true.
///
/// BREAKING: this endpoint used to return AuthResultDto directly. Any script or Swagger
/// client reading `.token` off the login response needs `.auth.token` instead.
/// </summary>
public class LoginResponseDto
{
    public bool MfaRequired { get; set; }
    public AuthResultDto? Auth { get; set; }
}

/// <summary>A TOTP code or a recovery code — the two are told apart by shape, not by a flag.</summary>
public class MfaVerifyDto
{
    [Required]
    [StringLength(32)]
    public string Code { get; set; } = string.Empty;
}

public class MfaSetupResultDto
{
    /// <summary>Base32 secret, for typing into an authenticator app by hand.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>otpauth:// URI — a tappable deep link into the authenticator on mobile.</summary>
    public string OtpAuthUri { get; set; } = string.Empty;
}

public class MfaEnableDto
{
    [Required]
    [StringLength(32)]
    public string Code { get; set; } = string.Empty;
}

/// <summary>Returned exactly once, at enrollment. There is no endpoint that shows them again.</summary>
public class MfaEnableResultDto
{
    public IReadOnlyList<string> RecoveryCodes { get; set; } = [];
}

public class MfaDisableDto
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(32)]
    public string Code { get; set; } = string.Empty;
}

public class MfaStatusDto
{
    public bool Enabled { get; set; }
    public DateTime? EnabledAt { get; set; }

    /// <summary>Drives the "you are running low on recovery codes" nag in the UI.</summary>
    public int RecoveryCodesRemaining { get; set; }
}
