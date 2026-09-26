namespace Sms.DomainService.Utilities;

/// <summary>
/// Whether a configured Telnyx webhook public key can actually authenticate anything.
/// </summary>
/// <remarks>
/// The Ed25519 verifier inside the Telnyx SDK accepts small-order public keys. With one of those
/// configured, a signature built from small-order points verifies for roughly one message in eight,
/// with no private key involved, so anyone could forge that tenant's delivery callbacks. Telnyx's
/// real keys are never small-order; a key that is must be a mistake or an attack, and is refused both
/// when saved and when a callback is checked (a stored one predates this check).
/// <para>
/// The list is libsodium's (<c>ge25519_has_small_order</c>): the small-order points and their
/// non-canonical encodings, compared with the sign bit cleared.
/// </para>
/// </remarks>
public static class TelnyxWebhookKey
{
    private static readonly byte[][] SmallOrderEncodings =
    [
        Convert.FromHexString("0000000000000000000000000000000000000000000000000000000000000000"),
        Convert.FromHexString("0100000000000000000000000000000000000000000000000000000000000000"),
        Convert.FromHexString("26e8958fc2b227b045c3f489f2ef98f0d5dfac05d3c63339b13802886d53fc05"),
        Convert.FromHexString("c7176a703d4dd84fba3c0b760d10670f2a2053fa2c39ccc64ec7fd7792ac037a"),
        Convert.FromHexString("ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f"),
        Convert.FromHexString("edffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f"),
        Convert.FromHexString("eeffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")
    ];

    /// <summary>Base64 of exactly 32 bytes, and not a small-order point.</summary>
    public static bool IsUsable(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        var key = new byte[32];
        if (!Convert.TryFromBase64String(base64.Trim(), key, out var written) || written != 32)
        {
            return false;
        }

        key[31] &= 0x7f;
        return !SmallOrderEncodings.Any(bad => bad.AsSpan().SequenceEqual(key));
    }
}
