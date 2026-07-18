using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class ProviderTokenProtectorTests
{
    [Fact]
    public void Protected_token_round_trips_without_plaintext_storage()
    {
        const string providerToken =
            "8415995487234100";
        var protector = CreateProtector();

        protector.TryProtect(
                providerToken,
                out var protectedToken)
            .Should()
            .BeTrue();

        protectedToken.Ciphertext.Should()
            .NotContain(providerToken);
        protectedToken.Fingerprint.Should()
            .NotBe(providerToken);

        var method = new StoredPaymentMethod
        {
            ProviderTokenCiphertext =
                protectedToken.Ciphertext,
            ProviderTokenFingerprint =
                protectedToken.Fingerprint,
            TokenEncryptionKeyId =
                protectedToken.EncryptionKeyId
        };

        protector.TryUnprotect(
                method,
                out var recoveredToken)
            .Should()
            .BeTrue();
        recoveredToken.Should().Be(providerToken);
    }

    [Fact]
    public void Token_protection_rejects_missing_key_configuration()
    {
        var options = new PaymentOptions();
        var monitor =
            new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.SetupGet(value => value.CurrentValue)
            .Returns(options);
        var protector =
            new ProviderTokenProtector(monitor.Object);

        protector.TryProtect("token", out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Fingerprint_is_deterministic_without_exposing_token()
    {
        var protector = CreateProtector();

        var first =
            protector.CreateFingerprint("provider-token");
        var second =
            protector.CreateFingerprint("provider-token");

        first.Should().Be(second);
        first.Should().NotContain("provider-token");
    }

    private static ProviderTokenProtector CreateProtector()
    {
        var options = new PaymentOptions
        {
            ActiveProviderTokenEncryptionKeyId = "key-1",
            ProviderTokenEncryptionKeys =
                new Dictionary<string, string>
                {
                    ["key-1"] =
                        Convert.ToBase64String(
                            Enumerable.Range(1, 32)
                                .Select(value => (byte)value)
                                .ToArray())
                }
        };
        var monitor =
            new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.SetupGet(value => value.CurrentValue)
            .Returns(options);

        return new ProviderTokenProtector(
            monitor.Object);
    }
}
