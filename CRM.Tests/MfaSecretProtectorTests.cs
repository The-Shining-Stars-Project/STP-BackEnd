using System.Security.Cryptography;
using CRM.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace CRM.Tests;

/// <summary>
/// AES-GCM protection for TOTP secrets. The properties worth pinning are the ones that turn a
/// database read into a dead end: fresh nonces, tamper detection, and the binding of a
/// ciphertext to the user row it sits in.
///
/// ThrowsAny rather than Throws throughout, because AesGcm raises
/// AuthenticationTagMismatchException, which IS a CryptographicException. The interface
/// promises the base type; pinning the exact derived one would be asserting on an
/// implementation detail of the platform's crypto backend.
/// </summary>
public class MfaSecretProtectorTests
{
    private static readonly Guid UserA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid UserB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static readonly byte[] Secret =
    [
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A,
        0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14,
    ];

    private readonly MfaSecretProtector _protector = TestMfa.Protector();

    [Fact]
    public void Round_trips_a_secret()
    {
        var blob = _protector.Protect(UserA, Secret);
        Assert.Equal(Secret, _protector.Unprotect(UserA, blob));
    }

    [Fact]
    public void Produces_a_different_blob_each_time()
    {
        // A fresh nonce per encryption. Reusing one under the same key is the single fastest
        // way to destroy GCM's guarantees, so this is worth an explicit test.
        Assert.NotEqual(_protector.Protect(UserA, Secret), _protector.Protect(UserA, Secret));
    }

    [Fact]
    public void Refuses_a_blob_encrypted_for_a_different_user()
    {
        // The AAD binding. Without it, a SQL write that copies A's encrypted secret onto B's
        // row would hand A's authenticator control of B's account; with it, B's next sign-in
        // fails instead.
        var blob = _protector.Protect(UserA, Secret);
        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect(UserB, blob));
    }

    [Fact]
    public void Refuses_a_blob_with_any_single_byte_flipped()
    {
        var bytes = Convert.FromBase64String(_protector.Protect(UserA, Secret));

        for (var i = 0; i < bytes.Length; i++)
        {
            var tampered = (byte[])bytes.Clone();
            tampered[i] ^= 0xFF;

            // Every position matters: version byte, nonce, tag and ciphertext alike. The point
            // is that tampering NEVER yields a plausible-but-wrong secret, only an exception.
            Assert.ThrowsAny<CryptographicException>(
                () => _protector.Unprotect(UserA, Convert.ToBase64String(tampered)));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!")]
    [InlineData("AAAA")] // valid base64, far too short to be a blob
    public void Refuses_malformed_stored_values(string stored) =>
        Assert.ThrowsAny<CryptographicException>(() => _protector.Unprotect(UserA, stored));

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(16)]
    public void Refuses_a_key_that_is_not_32_bytes(int keyBytes)
    {
        var settings = Options.Create(new MfaSettings
        {
            EncryptionKey = Convert.ToBase64String(new byte[keyBytes]),
        });

        // Fails at construction, not at first use — the service is a singleton, so a bad key
        // surfaces immediately rather than during somebody's sign-in.
        var ex = Assert.Throws<InvalidOperationException>(() => new MfaSecretProtector(settings));
        Assert.Contains("32 bytes", ex.Message);
    }

    [Fact]
    public void Refuses_a_key_that_is_not_base64()
    {
        var settings = Options.Create(new MfaSettings { EncryptionKey = "this is not base64 %%%" });
        Assert.Throws<InvalidOperationException>(() => new MfaSecretProtector(settings));
    }

    [Fact]
    public void Refuses_a_missing_key()
    {
        var settings = Options.Create(new MfaSettings { EncryptionKey = "" });
        var ex = Assert.Throws<InvalidOperationException>(() => new MfaSecretProtector(settings));
        Assert.Contains("Mfa:EncryptionKey", ex.Message);
    }

    [Fact]
    public void Cannot_be_decrypted_under_a_different_key()
    {
        var blob = _protector.Protect(UserA, Secret);
        var other = new MfaSecretProtector(Options.Create(new MfaSettings
        {
            EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        }));

        Assert.ThrowsAny<CryptographicException>(() => other.Unprotect(UserA, blob));
    }
}
