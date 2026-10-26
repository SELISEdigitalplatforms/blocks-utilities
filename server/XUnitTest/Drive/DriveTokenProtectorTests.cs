using FluentAssertions;
using Utility.DomainService.Drive.Service;

namespace XUnitTest.Drive;

/// <summary>
/// Guards the refresh token at rest: what is written to Mongo must not be the token, and a
/// tampered row must fail to decrypt rather than yield something that gets sent to Entra.
/// </summary>
public sealed class DriveTokenProtectorTests
{
    [Fact]
    public void Protect_then_unprotect_round_trips_and_ciphertext_hides_the_token()
    {
        var protector = new DriveTokenProtector(DriveTestSupport.Options());

        var ciphertext = protector.Protect("refresh-token-value");

        ciphertext.Should().NotBeNull();
        ciphertext.Should().NotContain("refresh-token-value",
            because: "a Mongo dump must not hand an attacker every user's drive");
        protector.Unprotect(ciphertext!).Should().Be("refresh-token-value");
    }

    [Fact]
    public void Two_protections_of_the_same_token_differ()
    {
        var protector = new DriveTokenProtector(DriveTestSupport.Options());

        protector.Protect("same").Should().NotBe(protector.Protect("same"),
            because: "a fresh nonce per write keeps equal tokens from being recognizable as equal");
    }

    [Fact]
    public void A_tampered_ciphertext_does_not_decrypt()
    {
        var protector = new DriveTokenProtector(DriveTestSupport.Options());
        var bytes = Convert.FromBase64String(protector.Protect("secret")!);
        bytes[^1] ^= 0x01;

        protector.Unprotect(Convert.ToBase64String(bytes)).Should().BeNull(
            because: "GCM is authenticated; an altered row must be rejected, not garbled");
    }

    [Fact]
    public void Without_a_key_the_protector_reports_not_configured_and_protects_nothing()
    {
        var protector = new DriveTokenProtector(DriveTestSupport.Options(o => o.TokenEncryptionKey = null));

        protector.IsConfigured.Should().BeFalse();
        protector.Protect("x").Should().BeNull(because: "a missing key must never fall back to plaintext");
    }
}
