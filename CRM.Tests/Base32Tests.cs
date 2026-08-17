using System.Text;
using CRM.Application.Auth;

namespace CRM.Tests;

/// <summary>
/// RFC 4648 §10 test vectors. The codec is hand-rolled, so this is the only thing standing
/// between a subtle bit-shift error and TOTP secrets that no authenticator app can read.
/// </summary>
public class Base32Tests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY======")]
    [InlineData("fo", "MZXQ====")]
    [InlineData("foo", "MZXW6===")]
    [InlineData("foob", "MZXW6YQ=")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI======")]
    public void Encodes_the_rfc_4648_vectors(string plain, string expected) =>
        Assert.Equal(expected, Base32.Encode(Encoding.ASCII.GetBytes(plain)));

    [Theory]
    [InlineData("", "")]
    [InlineData("MY======", "f")]
    [InlineData("MZXQ====", "fo")]
    [InlineData("MZXW6===", "foo")]
    [InlineData("MZXW6YQ=", "foob")]
    [InlineData("MZXW6YTB", "fooba")]
    [InlineData("MZXW6YTBOI======", "foobar")]
    public void Decodes_the_rfc_4648_vectors(string encoded, string expected)
    {
        Assert.True(Base32.TryDecode(encoded, out var bytes));
        Assert.Equal(expected, Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void Round_trips_arbitrary_bytes()
    {
        var original = new byte[] { 0x00, 0xFF, 0x7A, 0x01, 0x80, 0x42, 0x13, 0x99, 0xAB, 0xCD };
        Assert.True(Base32.TryDecode(Base32.Encode(original), out var decoded));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Decodes_lowercase()
    {
        Assert.True(Base32.TryDecode("mzxw6ytboi======", out var bytes));
        Assert.Equal("foobar", Encoding.ASCII.GetString(bytes));
    }

    [Theory]
    // Users paste secrets with the grouping the app displayed, and recovery codes are shown
    // with dashes. Both must decode to the same bytes as the bare form.
    [InlineData("MZXW 6YTB OI==  ====")]
    [InlineData("MZXW-6YTB-OI======")]
    public void Ignores_spaces_and_dashes(string spaced)
    {
        Assert.True(Base32.TryDecode(spaced, out var bytes));
        Assert.Equal("foobar", Encoding.ASCII.GetString(bytes));
    }

    [Theory]
    [InlineData("MZXW6YT1")]   // '1' is not in the alphabet
    [InlineData("MZXW6YT!")]
    [InlineData("MZXW6===YTB")] // data after padding
    [InlineData("MZX")]         // truncated mid-byte: 15 bits is not a whole number of bytes
    public void Rejects_malformed_input(string bad) =>
        Assert.False(Base32.TryDecode(bad, out _));

    [Fact]
    public void Rejects_null_without_throwing() =>
        Assert.False(Base32.TryDecode(null, out _));

    [Fact]
    public void Decodes_the_rfc_6238_seed()
    {
        // The cross-check that ties this codec to TotpServiceTests: the RFC 6238 seed, in the
        // base32 form an authenticator app would be given.
        Assert.True(Base32.TryDecode("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", out var bytes));
        Assert.Equal("12345678901234567890", Encoding.ASCII.GetString(bytes));
    }
}
