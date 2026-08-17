using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

public class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    /// <summary>Base64-encoded PBKDF2 hash of the password + salt.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Base64-encoded per-user random salt.</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Staff;

    public bool IsActive { get; set; } = true;

    /// <summary>Set only on a COMPLETED sign-in. For an MFA-enrolled user that means after
    /// the code is accepted, not after the password — a correct password alone is not a login.</summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Whether a second factor has been confirmed. The JWT carries a copy as the
    /// "mfa" claim, but that copy is informational — the gate reads this column.</summary>
    public bool MfaEnabled { get; set; }

    /// <summary>
    /// The TOTP shared secret, encrypted with AES-GCM under a key held outside the database
    /// (see MfaSecretProtector). Not hashed, because verifying a code requires the plaintext.
    /// Encrypted rather than stored bare because Azure SQL TDE protects the files on disk and
    /// does nothing whatsoever about a SQL-injection read, which is the threat that actually
    /// applies to a public web API. Null until /api/auth/mfa/setup; present but with
    /// MfaEnabled false while an enrollment is pending confirmation.
    /// </summary>
    public string? MfaSecret { get; set; }

    /// <summary>
    /// The highest TOTP step already accepted for this user. RFC 6238 §5.2: a code is valid
    /// for thirty seconds, so without this a shoulder-surfed code works until the window
    /// closes. Any step at or below this one is refused.
    /// </summary>
    public long LastTotpStep { get; set; }

    public DateTime? MfaEnabledAt { get; set; }

    /// <summary>
    /// Rejected second-factor codes since the last successful one, within the failure window.
    /// This is a per-ACCOUNT counter and it deliberately outlives any single MfaChallenge.
    ///
    /// The 5-attempt cap on MfaChallenge alone bounds nothing: superseding outstanding
    /// challenges stops an attacker holding several in PARALLEL, but re-running the password
    /// step mints a fresh row with the counter back at zero, so a sequential guessing loop
    /// costs one extra POST /api/auth/login per five guesses. Six digits do not survive that.
    /// The IP rate limiter is not the answer either — this API is publicly reachable, so an
    /// attacker calling it directly from rotating addresses gets a fresh budget per address.
    /// </summary>
    public int FailedMfaAttempts { get; set; }

    /// <summary>
    /// When the most recent rejected code was seen. Failures older than the window do not
    /// count, so a user who mistypes once a week is never locked out by accumulation.
    /// </summary>
    public DateTime? LastFailedMfaAt { get; set; }

    /// <summary>
    /// While set and in the future, a correct password for this account produces the same
    /// response as a wrong one and no challenge is issued. Refusing at the password step
    /// rather than at the code step is what keeps it from becoming an oracle: the caller
    /// cannot tell a locked account from a mistyped password.
    /// </summary>
    public DateTime? MfaLockedUntil { get; set; }

    /// <summary>Optimistic-concurrency token (#26) — concurrent full-row updates now
    /// surface as conflicts instead of silently overwriting each other.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Optional link to a staff record. Admins need not be staff.</summary>
    public Guid? StaffMemberId { get; set; }
    public StaffMember? StaffMember { get; set; }
}
