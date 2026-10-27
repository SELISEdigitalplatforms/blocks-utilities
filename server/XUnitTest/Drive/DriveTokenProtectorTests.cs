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
    public async Task Protect_then_unprotect_round_trips_and_ciphertext_hides_the_token()
    {
        var protector = new DriveTokenProtector(DriveTestSupport.Secrets());

        var ciphertext = await protector.ProtectAsync("refresh-token-value", CancellationToken.None);

        ciphertext.Should().NotBeNull();
        ciphertext.Should().NotContain("refresh-token-value",
            because: "a Mongo dump must not hand an attacker every user's drive");
        (await protector.UnprotectAsync(ciphertext!, CancellationToken.None)).Should().Be("refresh-token-value");
    }

    [Fact]
    public async Task Two_protections_of_the_same_token_differ()
    {
        var protector = new DriveTokenProtector(DriveTestSupport.Secrets());

        (await protector.ProtectAsync("same", CancellationToken.None)).Should().NotBe(await protector.ProtectAsync("same", CancellationToken.None),
            because: "a fresh nonce per write keeps equal tokens from being recognizable as equal");
    }

    [Fact]
    public async Task A_tampered_ciphertext_does_not_decrypt()
    {
        var protector = new DriveTokenProtector(DriveTestSupport.Secrets());
        var bytes = Convert.FromBase64String((await protector.ProtectAsync("secret", CancellationToken.None))!);
        bytes[^1] ^= 0x01;

        (await protector.UnprotectAsync(Convert.ToBase64String(bytes), CancellationToken.None)).Should().BeNull(
            because: "GCM is authenticated; an altered row must be rejected, not garbled");
    }

    [Fact]
    public async Task Without_a_key_in_blocks_secrets_the_protector_reports_not_configured_and_protects_nothing()
    {
        var protector = new DriveTokenProtector(DriveTestSupport.Secrets(key: null));

        (await protector.IsConfiguredAsync(CancellationToken.None)).Should().BeFalse();
        (await protector.ProtectAsync("x", CancellationToken.None)).Should().BeNull(because: "a missing key must never fall back to plaintext");
    }
}
