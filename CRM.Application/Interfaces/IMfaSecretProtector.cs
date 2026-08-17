namespace CRM.Application.Interfaces;

/// <summary>
/// Encrypts a user's TOTP shared secret for storage. Unlike a password, the secret must be
/// recoverable in plaintext to verify a code, so hashing is not an option — it is encrypted
/// under a key held outside the database.
/// </summary>
public interface IMfaSecretProtector
{
    /// <summary>Encrypts a secret, binding the ciphertext to the owning user's id.</summary>
    string Protect(Guid userId, byte[] secret);

    /// <summary>
    /// Decrypts a stored blob. Throws <see cref="System.Security.Cryptography.CryptographicException"/>
    /// if it was tampered with, truncated, or belongs to a different user — never returns a
    /// wrong answer.
    /// </summary>
    byte[] Unprotect(Guid userId, string stored);
}
