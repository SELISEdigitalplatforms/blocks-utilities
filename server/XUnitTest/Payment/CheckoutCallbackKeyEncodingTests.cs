using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

/// <summary>
/// A callback key may be configured as base64, as hex, or as raw text, and which one it is read
/// as decides the bytes the HMAC is taken with.
/// </summary>
/// <remarks>
/// Every existing test in this area uses a raw-text key, so the base64 and hex branches were
/// reachable only in production. That matters more than the usual untested-branch case: the three
/// encodings are tried in a fixed order, a value can be legible as more than one of them, and the
/// branch that wins produces different key bytes from the branch that would have. Re-keying a
/// deployment that way invalidates every callback token in flight, and nothing would fail until a
/// shopper returned from the provider.
/// <para>
/// These pin the order and the fallthrough, so it cannot be rearranged by accident.
/// </para>
/// </remarks>
public sealed class CheckoutCallbackKeyEncodingTests
{
    private static string RoundTrip(string key)
    {
        var protector = new CheckoutCallbackStateProtector();
        var issued = protector.Create(
            "tenant-a", null, "payment-1", "ADYEN-ONLINE", TimeSpan.FromMinutes(30), key);

        protector.TryUnprotect(issued.Token, key, null, out _).Should().BeTrue();
        return issued.Token;
    }

    [Fact]
    public void A_base64_key_is_accepted()
    {
        RoundTrip(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    [Fact]
    public void A_hex_key_is_accepted()
    {
        // 66 hex characters: an odd count of bytes keeps the length off a multiple of four, so
        // this reaches the hex branch rather than being swallowed as base64 first.
        RoundTrip(Convert.ToHexString(RandomNumberGenerator.GetBytes(33)));
    }

    [Fact]
    public void A_raw_text_key_is_accepted()
    {
        RoundTrip("return-state-key-that-is-longer-than-thirty-two-bytes");
    }

    /// <summary>
    /// The base64 attempt failing is the ordinary path for the other two encodings, not an error.
    /// </summary>
    [Fact]
    public void A_key_that_is_not_base64_falls_through_instead_of_failing()
    {
        // '-' is outside the base64 alphabet, so the decode throws and is deliberately swallowed.
        var act = () => RoundTrip("not-base64-but-still-far-longer-than-thirty-two-bytes");

        act.Should().NotThrow();
    }

    /// <summary>
    /// A value legible as two encodings is read as the first one that fits, and that is base64.
    /// </summary>
    [Fact]
    public void A_hex_looking_key_that_is_also_valid_base64_is_read_as_base64()
    {
        // 64 characters drawn from [0-9a-f]: valid hex, and also a valid base64 string whose
        // length is a multiple of four. Base64 is tried first, so these are the bytes used.
        const string Ambiguous =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        var expected = Convert.FromBase64String(Ambiguous);
        expected.Should().NotEqual(
            Convert.FromHexString(Ambiguous),
            "the two readings must actually differ, or this test proves nothing");

        var protector = new CheckoutCallbackStateProtector();
        var issued = protector.Create(
            "tenant-a", null, "payment-1", "ADYEN-ONLINE", TimeSpan.FromMinutes(30), Ambiguous);

        // Re-sign the payload with the base64 reading and require the same signature, which is
        // only true if the implementation chose base64 too.
        var parts = issued.Token.Split('.');
        var payload = FromBase64Url(parts[0]);
        var signature = FromBase64Url(parts[1]);

        HMACSHA256.HashData(expected, payload).Should().Equal(
            signature,
            "base64 is tried before hex, so an ambiguous key is keyed by its base64 bytes");
    }

    [Fact]
    public void A_key_shorter_than_256_bits_is_refused()
    {
        var protector = new CheckoutCallbackStateProtector();

        var act = () => protector.Create(
            "tenant-a", null, "payment-1", "ADYEN-ONLINE", TimeSpan.FromMinutes(30), "too-short");

        act.Should().Throw<FormatException>();
    }

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
