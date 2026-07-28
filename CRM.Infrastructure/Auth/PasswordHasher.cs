using System.Security.Cryptography;
using CRM.Application.Interfaces;

namespace CRM.Infrastructure.Auth;

/// <summary>
/// PBKDF2 (HMAC-SHA256) password hasher with a per-user random salt.
/// Both salt and derived key are stored Base64-encoded.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;        // 128-bit salt
    private const int KeySize = 32;         // 256-bit derived key
    private const int Iterations = 100_000; // OWASP-recommended floor for PBKDF2-HMAC-SHA256
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Salt used to burn an equivalent amount of CPU when there is no stored credential to
    /// check against (#4). Not a secret and never matches a real password — its only job is
    /// to make the "no such user" path cost the same as the "wrong password" path.
    /// </summary>
    private static readonly byte[] DummySalt = new byte[SaltSize];

    public (string Hash, string Salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
        return (Convert.ToBase64String(key), Convert.ToBase64String(salt));
    }

    public bool VerifyPassword(string password, string hash, string salt)
    {
        // Callers pass empty strings when no user matched the submitted email. Returning
        // early there would skip the 100k-iteration derivation and make a miss answer in
        // microseconds while a hit takes tens of milliseconds — a timing oracle that leaks
        // which email addresses have accounts (#4). Do the work anyway, then fail.
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt))
            return BurnEquivalentWork(password);

        byte[] saltBytes, expected;
        try
        {
            saltBytes = Convert.FromBase64String(salt);
            expected = Convert.FromBase64String(hash);
        }
        catch (FormatException)
        {
            // A corrupt stored credential is not an enumeration signal, but the same
            // reasoning applies — never let the shape of stored data change response time.
            return BurnEquivalentWork(password);
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, Algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Performs one derivation with the real parameters and discards it, so a failed lookup
    /// costs what a real verification costs. Always returns false.
    /// </summary>
    private static bool BurnEquivalentWork(string password)
    {
        var thrownAway = Rfc2898DeriveBytes.Pbkdf2(password, DummySalt, Iterations, Algorithm, KeySize);
        CryptographicOperations.ZeroMemory(thrownAway);
        return false;
    }
}
