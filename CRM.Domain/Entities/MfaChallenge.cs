using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// The half-finished login between the password step and the code step. A correct password
/// for an enrolled user mints one of these and NO session cookies; only exchanging it for a
/// valid TOTP or recovery code produces a real session.
///
/// Only a SHA-256 hash of the token is stored, for the same reason as RefreshToken: a
/// database read must not yield a usable credential. The hash is fast on purpose — the token
/// is 32 random bytes, so there is nothing for a slow KDF to protect.
///
/// Rows cascade with the user, unlike AuditEvent. These are ephemeral credentials, not
/// history: a deleted account's dead challenge tokens are noise, and keeping them would
/// preserve nothing anyone would ever look at.
/// </summary>
public class MfaChallenge : BaseEntity
{
    /// <summary>
    /// Total code guesses allowed against one challenge. Lives on the entity rather than in
    /// AuthService because <see cref="IsUsable"/> needs it too, and two copies of a security
    /// limit is one copy too many.
    /// </summary>
    public const int MaxAttempts = 5;

    public Guid UserId { get; set; }

    /// <summary>Base64 SHA-256 of the raw token placed in the ss_mfa cookie.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Set the moment the challenge is spent or killed. Single-use, both ways.</summary>
    public DateTime? ConsumedAt { get; set; }

    /// <summary>
    /// Set alongside <see cref="ConsumedAt"/> when the system killed this challenge rather than
    /// the user spending it — a second sign-in on another device superseded it, or an admin
    /// reset the account's second factor.
    ///
    /// It exists so the AUDIT LOG can tell those apart from a genuine replay. Presenting a
    /// consumed token is either somebody replaying a captured cookie or a teacher who started
    /// signing in on the staff-room laptop, finished on their phone, and went back to the
    /// laptop. Both are refused identically — that part is deliberate and must not change — but
    /// a log read by a human should not label the second one as an attack.
    /// </summary>
    public DateTime? SupersededAt { get; set; }

    public int AttemptCount { get; set; }

    public User User { get; set; } = null!;

    public bool IsUsable =>
        ConsumedAt is null && DateTime.UtcNow < ExpiresAt && AttemptCount < MaxAttempts;
}
