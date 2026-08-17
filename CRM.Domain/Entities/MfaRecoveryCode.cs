using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// One single-use way back in when the authenticator app is gone. Ten are issued at
/// enrollment, shown exactly once, and each dies the first time it is used.
///
/// Stored as a plain SHA-256 hash rather than PBKDF2, and that is deliberate: these are 80
/// bits of randomness this server generated, not something a human chose. A slow KDF defends
/// against guessing a low-entropy secret; there is nothing here to guess. Same reasoning as
/// RefreshToken.
///
/// Cascades with the user — see MfaChallenge.
/// </summary>
public class MfaRecoveryCode : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>Base64 SHA-256 of the normalized (uppercase, separators stripped) code.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTime? UsedAt { get; set; }

    public User User { get; set; } = null!;
}
