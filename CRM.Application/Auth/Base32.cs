using System.Text;

namespace CRM.Application.Auth;

/// <summary>
/// RFC 4648 base32 (standard alphabet, '=' padding). Hand-rolled for the same reason
/// PasswordHasher is: .NET ships no base32 codec, the RFC publishes test vectors, and the
/// algorithm is thirty lines. TOTP secrets are exchanged with authenticator apps in this
/// encoding, and recovery codes are generated in it because it has no visually ambiguous
/// characters (no 0/O, no 1/I/l) — the codes get read off paper.
///
/// This lives in CRM.Application rather than CRM.Infrastructure alongside TotpService
/// because AuthService needs it to mint recovery codes, and CRM.Application must not
/// reference CRM.Infrastructure. It is pure and dependency-free, so it belongs on the
/// inner layer either way.
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Number of base32 characters carrying data for a 1..5 byte final group.</summary>
    private static readonly int[] DataCharsForChunk = [0, 2, 4, 5, 7, 8];

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return string.Empty;

        var output = new StringBuilder((data.Length + 4) / 5 * 8);

        for (var i = 0; i < data.Length; i += 5)
        {
            var chunk = Math.Min(5, data.Length - i);

            // Pack up to 40 bits big-endian; absent bytes are zero, and the characters they
            // would have produced become '=' below.
            ulong buffer = 0;
            for (var j = 0; j < 5; j++)
                buffer = (buffer << 8) | (j < chunk ? data[i + j] : 0UL);

            var dataChars = DataCharsForChunk[chunk];
            for (var j = 0; j < 8; j++)
                output.Append(j < dataChars ? Alphabet[(int)((buffer >> (35 - j * 5)) & 0x1F)] : '=');
        }

        return output.ToString();
    }

    /// <summary>
    /// Decodes base32, tolerating lowercase and the spaces or dashes people paste along with
    /// a secret. Returns false rather than throwing: every caller here is handling untrusted
    /// input, and an exception on a typo is the wrong shape of failure.
    /// </summary>
    public static bool TryDecode(string? input, out byte[] bytes)
    {
        bytes = [];
        if (input is null) return false;

        var output = new List<byte>(input.Length * 5 / 8 + 1);
        ulong buffer = 0;
        var bits = 0;
        var padded = false;

        foreach (var raw in input)
        {
            if (raw is ' ' or '-' or '\t' or '\r' or '\n') continue;

            if (raw == '=')
            {
                padded = true;
                continue;
            }

            // Data after padding is malformed, not merely odd — accepting it would let two
            // different strings decode to the same bytes.
            if (padded) return false;

            var value = Alphabet.IndexOf(char.ToUpperInvariant(raw));
            if (value < 0) return false;

            buffer = (buffer << 5) | (uint)value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        // A whole unconsumed symbol means the input was truncated mid-byte, and any leftover
        // bits must be the zero padding the encoder wrote. Both checks exist so that a
        // corrupted secret is rejected rather than silently decoding to something shorter.
        if (bits >= 5) return false;
        if (bits > 0 && (buffer & ((1UL << bits) - 1)) != 0) return false;

        bytes = output.ToArray();
        return true;
    }
}
