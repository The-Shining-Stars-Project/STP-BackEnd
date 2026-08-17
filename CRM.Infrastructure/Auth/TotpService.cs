using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CRM.Application.Auth;
using CRM.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace CRM.Infrastructure.Auth;

/// <summary>
/// RFC 6238 TOTP, hand-rolled for the same reason PasswordHasher is: .NET ships HMACSHA1,
/// the RFC publishes test vectors, and a dependency for eighty lines of well-specified
/// arithmetic is a supply-chain risk with no upside. TotpServiceTests runs the RFC 6238
/// Appendix B vectors against it.
/// </summary>
public class TotpService : ITotpService
{
    /// <summary>RFC 6238 defaults — what every mainstream authenticator app assumes.</summary>
    public const int StepSeconds = 30;
    public const int Digits = 6;

    /// <summary>
    /// ±1 step. Phones drift and users type slowly, so a zero-skew window rejects codes that
    /// were correct when the user read them. Two steps either side would nearly double the
    /// guessing surface for no practical gain.
    /// </summary>
    private const int SkewSteps = 1;

    private const int SecretBytes = 20; // 160-bit, matching what authenticator apps generate

    private static readonly int[] PowersOfTen = [1, 10, 100, 1_000, 10_000, 100_000, 1_000_000, 10_000_000, 100_000_000];

    private readonly MfaSettings _settings;

    public TotpService(IOptions<MfaSettings> settings) => _settings = settings.Value;

    public byte[] GenerateSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    public string ToBase32(byte[] secret) => Base32.Encode(secret);

    public string BuildOtpAuthUri(string accountEmail, byte[] secret)
    {
        // The label is "Issuer:account" and the issuer is repeated as a parameter — both are
        // required by the key-uri format, and apps that read only one of the two still show
        // the right name.
        var issuer = _settings.Issuer;
        var label = Uri.EscapeDataString($"{issuer}:{accountEmail}");
        return $"otpauth://totp/{label}"
               + $"?secret={Base32.Encode(secret).TrimEnd('=')}"
               + $"&issuer={Uri.EscapeDataString(issuer)}"
               + $"&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    public (TotpValidationResult Result, long Step) Validate(
        byte[] secret, string? code, long lastAcceptedStep, DateTimeOffset now)
    {
        var normalized = Normalize(code);
        if (normalized is null) return (TotpValidationResult.Rejected, 0);

        var presented = Encoding.UTF8.GetBytes(normalized);
        var current = ComputeStep(now.ToUnixTimeSeconds());

        for (var offset = -SkewSteps; offset <= SkewSteps; offset++)
        {
            var step = current + offset;
            var expected = Encoding.UTF8.GetBytes(ComputeCode(secret, step, Digits));
            if (!CryptographicOperations.FixedTimeEquals(expected, presented)) continue;

            // A match at or below the last accepted step is a REPLAYED code, not a wrong one,
            // and the difference matters when reading the audit log: a replay means someone
            // other than the user saw a code the user had already spent. RFC 6238 §5.2
            // requires refusing it either way.
            return step > lastAcceptedStep
                ? (TotpValidationResult.Accepted, step)
                : (TotpValidationResult.Replayed, step);
        }

        return (TotpValidationResult.Rejected, 0);
    }

    /// <summary>
    /// RFC 6238 step number: T = (now - T0) / X, with T0 = 0 and X = 30.
    /// Public so the RFC Appendix B vectors can be asserted directly.
    /// </summary>
    public static long ComputeStep(long unixSeconds) => unixSeconds / StepSeconds;

    /// <summary>
    /// HOTP (RFC 4226 §5.3/§5.4) over a big-endian step counter: HMAC-SHA1, dynamic
    /// truncation, modulo 10^digits, zero-padded. Public so the vectors can be asserted at
    /// both 8 digits (as the RFC publishes them) and the 6 this app uses.
    /// </summary>
    public static string ComputeCode(byte[] key, long step, int digits)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        Span<byte> hmac = stackalloc byte[20];
        HMACSHA1.HashData(key, counter, hmac);

        var offset = hmac[19] & 0x0F;
        var binary = ((hmac[offset] & 0x7F) << 24)
                     | ((hmac[offset + 1] & 0xFF) << 16)
                     | ((hmac[offset + 2] & 0xFF) << 8)
                     | (hmac[offset + 3] & 0xFF);

        return (binary % PowersOfTen[digits]).ToString().PadLeft(digits, '0');
    }

    /// <summary>
    /// Strips the spaces and dashes authenticator apps display between digit groups, then
    /// requires exactly six digits. Returns null for anything else, which is how the caller
    /// tells a TOTP code apart from a recovery code.
    /// </summary>
    private static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        Span<char> digits = stackalloc char[Digits];
        var length = 0;

        foreach (var c in code)
        {
            if (c is ' ' or '-' or '\t') continue;
            if (!char.IsAsciiDigit(c)) return null;
            if (length == Digits) return null; // too long
            digits[length++] = c;
        }

        return length == Digits ? new string(digits) : null;
    }
}
