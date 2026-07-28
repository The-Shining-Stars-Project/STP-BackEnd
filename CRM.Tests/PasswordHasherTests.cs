using System.Diagnostics;
using CRM.Infrastructure.Auth;

namespace CRM.Tests;

/// <summary>
/// Cover for #4 — the "no such user" path used to skip the key derivation entirely, so login
/// response time told an attacker which email addresses had accounts.
/// </summary>
public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void A_correct_password_verifies()
    {
        var (hash, salt) = _hasher.HashPassword("correct horse battery staple");

        Assert.True(_hasher.VerifyPassword("correct horse battery staple", hash, salt));
    }

    [Fact]
    public void A_wrong_password_does_not_verify()
    {
        var (hash, salt) = _hasher.HashPassword("correct horse battery staple");

        Assert.False(_hasher.VerifyPassword("Correct Horse Battery Staple", hash, salt));
    }

    [Fact]
    public void Each_hash_uses_a_fresh_salt()
    {
        var (hashA, saltA) = _hasher.HashPassword("same password");
        var (hashB, saltB) = _hasher.HashPassword("same password");

        Assert.NotEqual(saltA, saltB);
        Assert.NotEqual(hashA, hashB);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("not-base64!!", "also-not-base64!!")]
    public void Missing_or_malformed_credentials_fail(string hash, string salt)
    {
        Assert.False(_hasher.VerifyPassword("anything", hash, salt));
    }

    [Fact]
    public void Verifying_against_a_missing_credential_costs_the_same_as_a_real_check()
    {
        // The whole point of the fix: a lookup miss must not answer faster than a real
        // verification. Before it, the miss path returned without touching PBKDF2 and was
        // ~1000x quicker; now both run one 100k-iteration derivation.
        //
        // Asserted as a ratio so machine speed and CI load cancel out, with a wide band —
        // the real value sits near 1.0 and the pre-fix value near 0.001, so anything above
        // 0.25 distinguishes them without being flaky.
        var (hash, salt) = _hasher.HashPassword("a real password");

        // Warm up the JIT so first-call cost lands on neither measurement.
        _hasher.VerifyPassword("x", hash, salt);
        _hasher.VerifyPassword("x", "", "");

        const int rounds = 5;
        var hit = Measure(() => _hasher.VerifyPassword("wrong password", hash, salt), rounds);
        var miss = Measure(() => _hasher.VerifyPassword("wrong password", "", ""), rounds);

        Assert.True(
            miss.TotalMilliseconds > hit.TotalMilliseconds * 0.25,
            $"The missing-credential path returned far too quickly: {miss.TotalMilliseconds:F1}ms "
            + $"versus {hit.TotalMilliseconds:F1}ms for a real verification. The timing oracle is back.");
    }

    private static TimeSpan Measure(Action action, int rounds)
    {
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < rounds; i++) action();
        sw.Stop();
        return sw.Elapsed;
    }
}
