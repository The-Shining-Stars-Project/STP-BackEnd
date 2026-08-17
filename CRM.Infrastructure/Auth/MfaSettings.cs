namespace CRM.Infrastructure.Auth;

/// <summary>
/// Bound from the "Mfa" section of configuration.
///
/// Only the values that Infrastructure or the API pipeline need live here. The challenge
/// lifetime, the attempt cap and the recovery-code count are deliberately absent: they are
/// needed by AuthService in CRM.Application, which references neither Options nor
/// Infrastructure, so they are private consts there alongside RefreshTokenDays.
/// </summary>
public class MfaSettings
{
    /// <summary>
    /// Base64 of exactly 32 random bytes (AES-256). Generate with <c>openssl rand -base64 32</c>.
    ///
    /// There is no key-rotation story: lose or change this and every enrolled user's secret
    /// becomes undecryptable, dropping the whole team onto recovery codes and then admin
    /// resets. The version byte in MfaSecretProtector's wire format reserves room to add
    /// rotation later; doing it is out of scope, and pretending otherwise would be worse than
    /// saying so.
    /// </summary>
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>
    /// Whether an enrolled second factor is required to use the app. Defaults to true, and
    /// the enforcement is server-side (MfaEnforcementFilter) rather than a UI redirect.
    /// </summary>
    public bool Required { get; set; } = true;

    /// <summary>Shown as the account issuer in the authenticator app.</summary>
    public string Issuer { get; set; } = "Shining Stars CRM";
}
