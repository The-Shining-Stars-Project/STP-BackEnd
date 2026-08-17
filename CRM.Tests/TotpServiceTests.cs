using System.Text;
using CRM.Application.Interfaces;
using CRM.Infrastructure.Auth;

namespace CRM.Tests;

/// <summary>
/// RFC 6238 Appendix B, which is the whole reason hand-rolling TOTP is a reasonable thing to
/// do: the algorithm ships with published vectors, so "it looks right" is never the standard.
/// If these pass, an authenticator app will agree with this server.
/// </summary>
public class TotpServiceTests
{
    /// <summary>The RFC's SHA-1 seed: ASCII "12345678901234567890", 20 bytes.</summary>
    private static readonly byte[] Seed = Encoding.ASCII.GetBytes("12345678901234567890");

    private readonly TotpService _totp = TestMfa.Totp();

    // ---------- RFC 6238 Appendix B ----------

    [Theory]
    [InlineData(59L, 1L)]
    [InlineData(1111111109L, 37037036L)]
    [InlineData(1111111111L, 37037037L)]
    [InlineData(1234567890L, 41152263L)]
    [InlineData(2000000000L, 66666666L)]
    [InlineData(20000000000L, 666666666L)]
    public void Computes_the_rfc_step_numbers(long unixSeconds, long expectedStep) =>
        Assert.Equal(expectedStep, TotpService.ComputeStep(unixSeconds));

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Matches_the_rfc_8_digit_vectors(long unixSeconds, string expected) =>
        Assert.Equal(expected, TotpService.ComputeCode(Seed, TotpService.ComputeStep(unixSeconds), 8));

    [Theory]
    // The six-digit values this app actually issues, derived from the same vectors. Valid
    // because truncation is a modulo: (bin % 10^8) % 10^6 == bin % 10^6.
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Matches_the_rfc_vectors_at_6_digits(long unixSeconds, string expected) =>
        Assert.Equal(expected, TotpService.ComputeCode(Seed, TotpService.ComputeStep(unixSeconds), 6));

    [Fact]
    public void Zero_pads_short_codes()
    {
        // 1234567890 truncates to 5924, which is only a valid code if it is rendered "005924".
        // Trimming the zeros would produce a code the user cannot type and the app cannot match.
        var code = TotpService.ComputeCode(Seed, TotpService.ComputeStep(1234567890L), 6);
        Assert.Equal(6, code.Length);
        Assert.StartsWith("00", code);
    }

    // ---------- clock skew ----------

    private static DateTimeOffset At(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void Accepts_codes_one_step_either_side(int offset)
    {
        var now = At(1_700_000_000);
        var step = TotpService.ComputeStep(now.ToUnixTimeSeconds()) + offset;
        var code = TotpService.ComputeCode(Seed, step, 6);

        var (result, accepted) = _totp.Validate(Seed, code, lastAcceptedStep: 0, now);

        Assert.Equal(TotpValidationResult.Accepted, result);
        Assert.Equal(step, accepted);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(2)]
    public void Rejects_codes_two_steps_away(int offset)
    {
        var now = At(1_700_000_000);
        var step = TotpService.ComputeStep(now.ToUnixTimeSeconds()) + offset;
        var code = TotpService.ComputeCode(Seed, step, 6);

        var (result, _) = _totp.Validate(Seed, code, lastAcceptedStep: 0, now);

        Assert.Equal(TotpValidationResult.Rejected, result);
    }

    // ---------- replay (RFC 6238 §5.2) ----------

    [Fact]
    public void Refuses_a_code_for_a_step_already_spent()
    {
        var now = At(1_700_000_000);
        var step = TotpService.ComputeStep(now.ToUnixTimeSeconds());
        var code = TotpService.ComputeCode(Seed, step, 6);

        // Same code, same 30-second window, but the step has already been accepted once. This
        // is the shoulder-surfing case: without it the code stays usable until the window ends.
        var (result, _) = _totp.Validate(Seed, code, lastAcceptedStep: step, now);

        Assert.Equal(TotpValidationResult.Replayed, result);
    }

    [Fact]
    public void Refuses_an_earlier_step_than_the_last_accepted_one()
    {
        var now = At(1_700_000_000);
        var current = TotpService.ComputeStep(now.ToUnixTimeSeconds());
        var code = TotpService.ComputeCode(Seed, current - 1, 6);

        var (result, _) = _totp.Validate(Seed, code, lastAcceptedStep: current, now);

        Assert.Equal(TotpValidationResult.Replayed, result);
    }

    [Fact]
    public void A_replay_is_reported_differently_from_a_wrong_code()
    {
        // The audit log distinguishes these two, so the service must be able to.
        var now = At(1_700_000_000);
        var step = TotpService.ComputeStep(now.ToUnixTimeSeconds());

        var replayed = _totp.Validate(Seed, TotpService.ComputeCode(Seed, step, 6), step, now).Result;
        var wrong = _totp.Validate(Seed, "000000" == TotpService.ComputeCode(Seed, step, 6) ? "111111" : "000000", 0, now).Result;

        Assert.Equal(TotpValidationResult.Replayed, replayed);
        Assert.Equal(TotpValidationResult.Rejected, wrong);
    }

    // ---------- input handling ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc123")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12 34 5")]
    [InlineData("<script>")]
    public void Rejects_garbage_without_throwing(string? code)
    {
        var (result, _) = _totp.Validate(Seed, code, lastAcceptedStep: 0, At(1_700_000_000));
        Assert.Equal(TotpValidationResult.Rejected, result);
    }

    [Fact]
    public void Accepts_a_code_typed_with_the_grouping_the_app_displays()
    {
        var now = At(1_700_000_000);
        var step = TotpService.ComputeStep(now.ToUnixTimeSeconds());
        var code = TotpService.ComputeCode(Seed, step, 6);
        var spaced = $"{code[..3]} {code[3..]}";

        Assert.Equal(TotpValidationResult.Accepted, _totp.Validate(Seed, spaced, 0, now).Result);
    }

    // ---------- provisioning ----------

    [Fact]
    public void Generates_a_160_bit_secret() =>
        Assert.Equal(20, _totp.GenerateSecret().Length);

    [Fact]
    public void Generates_a_different_secret_every_time() =>
        Assert.NotEqual(_totp.GenerateSecret(), _totp.GenerateSecret());

    [Fact]
    public void Builds_an_otpauth_uri_an_authenticator_can_read()
    {
        var uri = _totp.BuildOtpAuthUri("teacher@example.org", Seed);

        Assert.StartsWith("otpauth://totp/", uri);
        // The issuer appears both in the label and as a parameter; apps read one or the other.
        Assert.Contains("Shining%20Stars%20CRM%3Ateacher%40example.org", uri);
        Assert.Contains("issuer=Shining%20Stars%20CRM", uri);
        Assert.Contains("algorithm=SHA1", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
        // Padding is stripped: several popular apps choke on '=' in the secret parameter.
        Assert.Contains("secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", uri);
        Assert.DoesNotContain("secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ=", uri);
    }
}
