namespace CRM.Application.Interfaces;

/// <summary>How a presented code was judged. See <see cref="ITotpService.Validate"/>.</summary>
public enum TotpValidationResult
{
    /// <summary>Not a valid code for any step in the accepted window.</summary>
    Rejected,

    /// <summary>
    /// A genuine code for a step that has already been used. RFC 6238 §5.2 requires refusing
    /// it, and it is worth telling apart from a wrong code: a replay means somebody other
    /// than the user saw a code the user already spent.
    /// </summary>
    Replayed,

    /// <summary>Valid, and for a step later than the last one accepted for this user.</summary>
    Accepted,
}

/// <summary>
/// RFC 6238 TOTP: 6 digits, 30-second step, SHA-1 — what Google Authenticator, Authy,
/// 1Password and the rest expect. SHA-1 is not a weakness here: HMAC-SHA1 has no practical
/// break, and the codes live 30 seconds.
/// </summary>
public interface ITotpService
{
    /// <summary>A fresh 160-bit shared secret, the size real authenticator apps emit.</summary>
    byte[] GenerateSecret();

    /// <summary>Base32 for manual entry into an authenticator app.</summary>
    string ToBase32(byte[] secret);

    /// <summary>
    /// The <c>otpauth://</c> provisioning URI. On a phone this is a tappable deep link
    /// straight into the authenticator; on a desktop the user types the base32 secret.
    /// </summary>
    string BuildOtpAuthUri(string accountEmail, byte[] secret);

    /// <summary>
    /// Checks a presented code against the secret, accepting the current step and one step
    /// either side of it, and refusing any step at or below <paramref name="lastAcceptedStep"/>.
    /// The returned step is meaningful only for <see cref="TotpValidationResult.Accepted"/>
    /// and <see cref="TotpValidationResult.Replayed"/>; the caller stores it to close the
    /// replay window.
    /// </summary>
    (TotpValidationResult Result, long Step) Validate(
        byte[] secret, string? code, long lastAcceptedStep, DateTimeOffset now);
}
