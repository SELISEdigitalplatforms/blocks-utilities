using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.Webhooks;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class AdyenWebhookSignatureVerifierTests
{
    private const string Canonical = "psp-1::merchant:payment-1:1050:USD:AUTHORISATION:true";

    private readonly AdyenWebhookSignatureVerifier _verifier = new();

    [Fact]
    public void Supports_only_the_adyen_online_provider()
    {
        _verifier.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeTrue();
        _verifier.Supports("STRIPE").Should().BeFalse();
    }

    [Fact]
    public void Standard_signature_is_verified_and_tampering_is_rejected()
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var provider = Provider(standardKey: key);

        _verifier.Verify(provider, Standard(Canonical, Sign(Convert.FromHexString(key), Canonical)))
            .Should().Be(WebhookSignatureOutcome.Valid);

        _verifier.Verify(provider, Standard(Canonical + "x", Sign(Convert.FromHexString(key), Canonical)))
            .Should().Be(WebhookSignatureOutcome.Invalid);
    }

    [Fact]
    public void Standard_signature_accepts_the_previous_key_during_rotation()
    {
        var previous = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var rotated = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var provider = Provider(standardKey: rotated);
        provider.PreviousStandardWebhookHmacKey = previous;

        _verifier.Verify(provider, Standard(Canonical, Sign(Convert.FromHexString(previous), Canonical)))
            .Should().Be(WebhookSignatureOutcome.Valid);
    }

    [Fact]
    public void Token_signature_covers_the_untouched_body()
    {
        const string key = "token-webhook-key-that-is-longer-than-thirty-two-bytes";
        const string body = "{\"id\":\"event-1\",\"type\":\"recurring.token.created\"}";
        var provider = Provider(tokenKey: key);
        var signature = Sign(Encoding.UTF8.GetBytes(key), body);

        _verifier.Verify(provider, Token(body, signature))
            .Should().Be(WebhookSignatureOutcome.Valid);
        _verifier.Verify(provider, Token(body + " ", signature))
            .Should().Be(WebhookSignatureOutcome.Invalid);
    }

    [Fact]
    public void Token_signature_accepts_a_hex_encoded_key()
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        const string body = "{\"id\":\"event-1\"}";
        var signature = Sign(Convert.FromHexString(key), body);

        _verifier.Verify(Provider(tokenKey: key), Token(body, signature))
            .Should().Be(WebhookSignatureOutcome.Valid);
    }

    [Fact]
    public void Missing_secret_is_reported_as_not_configured() =>
        _verifier.Verify(
                new PaymentProvider { ProviderName = PaymentConstants.AdyenOnlineProvider },
                Standard(Canonical, "signature"))
            .Should().Be(WebhookSignatureOutcome.NotConfigured);

    [Fact]
    public void Unknown_secret_name_is_reported_as_not_configured() =>
        _verifier.Verify(
                Provider(standardKey: Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
                new WebhookSignature(Canonical, "signature", "unknown"))
            .Should().Be(WebhookSignatureOutcome.NotConfigured);

    [Fact]
    public void Malformed_signature_encoding_is_rejected_without_throwing() =>
        _verifier.Verify(
                Provider(standardKey: Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
                Standard(Canonical, "not-base64!"))
            .Should().Be(WebhookSignatureOutcome.Invalid);

    private static string Sign(byte[] key, string payload) =>
        Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));

    private static WebhookSignature Standard(string payload, string signature) =>
        new(payload, signature, "standard");

    private static WebhookSignature Token(string payload, string signature) =>
        new(payload, signature, "token");

    private static PaymentProvider Provider(
        string? standardKey = null,
        string? tokenKey = null) => new()
    {
        ProviderName = PaymentConstants.AdyenOnlineProvider,
        StandardWebhookHmacKey = standardKey,
        TokenWebhookHmacKey = tokenKey
    };
}
