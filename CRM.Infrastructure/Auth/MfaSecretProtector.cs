using System.Security.Cryptography;
using CRM.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace CRM.Infrastructure.Auth;

/// <summary>
/// AES-256-GCM protection for TOTP secrets at rest.
///
/// Azure SQL TDE encrypts the database files on disk and does nothing at all about a
/// SQL-injection read or a leaked backup opened by a client that has the key — both hand
/// over plaintext columns. A key held in App Service configuration, outside the database, is
/// what makes a stolen copy of the Users table useless for minting codes.
/// </summary>
public class MfaSecretProtector : IMfaSecretProtector
{
    /// <summary>
    /// Leading byte of every blob. It is the only thing that makes a future key rotation
    /// possible without guessing: a rotation ships version 0x02 written under the new key
    /// while 0x01 still reads under the old one. Rotation itself is not implemented — see
    /// MfaSettings.EncryptionKey.
    /// </summary>
    private const byte Version = 0x01;

    private const int NonceSize = 12;                    // 96-bit, the GCM standard
    private static readonly int TagSize = AesGcm.TagByteSizes.MaxSize; // 16 bytes
    private const int KeySize = 32;                      // AES-256

    private readonly byte[] _key;

    public MfaSecretProtector(IOptions<MfaSettings> settings)
    {
        var configured = settings.Value.EncryptionKey;
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "Missing configuration: Mfa:EncryptionKey — set the Mfa__EncryptionKey environment variable in Azure App Service.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Mfa:EncryptionKey is not valid base64. Generate one with `openssl rand -base64 32`.");
        }

        if (key.Length != KeySize)
            throw new InvalidOperationException(
                $"Mfa:EncryptionKey must decode to exactly {KeySize} bytes (AES-256); it decoded to {key.Length}. "
                + "Generate one with `openssl rand -base64 32`.");

        _key = key;
    }

    public string Protect(Guid userId, byte[] secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[secret.Length];
        var tag = new byte[TagSize];

        // The tag size is passed explicitly: the parameterless-tag AesGcm constructor is
        // obsolete from .NET 8 and would break the zero-warning build.
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, secret, ciphertext, tag, Aad(userId));

        var blob = new byte[1 + NonceSize + TagSize + ciphertext.Length];
        blob[0] = Version;
        nonce.CopyTo(blob, 1);
        tag.CopyTo(blob, 1 + NonceSize);
        ciphertext.CopyTo(blob, 1 + NonceSize + TagSize);
        return Convert.ToBase64String(blob);
    }

    public byte[] Unprotect(Guid userId, string stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            throw new CryptographicException("No stored MFA secret.");

        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(stored);
        }
        catch (FormatException)
        {
            throw new CryptographicException("Stored MFA secret is not valid base64.");
        }

        if (blob.Length <= 1 + NonceSize + TagSize)
            throw new CryptographicException("Stored MFA secret is truncated.");
        if (blob[0] != Version)
            throw new CryptographicException($"Unsupported MFA secret format version {blob[0]}.");

        var nonce = blob.AsSpan(1, NonceSize);
        var tag = blob.AsSpan(1 + NonceSize, TagSize);
        var ciphertext = blob.AsSpan(1 + NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TagSize);
        // Throws AuthenticationTagMismatchException (a CryptographicException) on any
        // tampering, including a mismatched user id.
        aes.Decrypt(nonce, ciphertext, tag, plaintext, Aad(userId));
        return plaintext;
    }

    /// <summary>
    /// Additional authenticated data: the owning user's id. This binds the ciphertext to the
    /// row it sits in, so an attacker with a SQL write (but not the key) cannot copy user A's
    /// encrypted secret onto user B and gain control of B's account with A's authenticator —
    /// the tag check fails and the login is refused instead. It costs nothing.
    /// </summary>
    private static byte[] Aad(Guid userId) => userId.ToByteArray();
}
