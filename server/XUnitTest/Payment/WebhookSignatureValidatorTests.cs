using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class WebhookSignatureValidatorTests
{
    [Fact]
    public void Standard_signature_is_verified_and_tampering_is_rejected()
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var item = new NotificationItem
        {
            PspReference = "psp-1",
            MerchantAccountCode = "merchant",
            MerchantReference = "payment-1",
            Amount = new ProviderAmount { Value = 1050, Currency = "USD" },
            EventCode = "AUTHORISATION",
            Success = "true"
        };
        var canonical = "psp-1::merchant:payment-1:1050:USD:AUTHORISATION:true";
        item.AdditionalData["hmacSignature"] = Convert.ToBase64String(
            HMACSHA256.HashData(Convert.FromHexString(key), Encoding.UTF8.GetBytes(canonical)));
        var validator = new WebhookSignatureValidator();

        validator.ValidateStandard(item, key, null).Should().BeTrue();
        item.Amount.Value++;
        validator.ValidateStandard(item, key, null).Should().BeFalse();
    }

    [Fact]
    public void Token_signature_covers_the_untouched_body()
    {
        const string key = "token-webhook-key-that-is-longer-than-thirty-two-bytes";
        const string body = "{\"id\":\"event-1\",\"type\":\"recurring.token.created\"}";
        var signature = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(body)));
        var validator = new WebhookSignatureValidator();

        validator.ValidateToken(body, signature, key, null).Should().BeTrue();
        validator.ValidateToken(body + " ", signature, key, null).Should().BeFalse();
    }

    [Fact]
    public void Token_signature_accepts_Adyen_hex_encoded_key()
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        const string body = "{\"id\":\"event-1\",\"type\":\"recurring.token.created\"}";
        var signature = Convert.ToBase64String(
            HMACSHA256.HashData(
                Convert.FromHexString(key),
                Encoding.UTF8.GetBytes(body)));
        var validator = new WebhookSignatureValidator();
        var rotatedKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        validator.ValidateToken(body, signature, key, null).Should().BeTrue();
        validator.ValidateToken(body, signature, rotatedKey, key).Should().BeTrue();
        validator.ValidateToken(body + " ", signature, key, null).Should().BeFalse();
    }
}
