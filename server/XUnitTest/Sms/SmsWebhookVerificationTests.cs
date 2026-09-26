using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sms.DomainService.Dtos;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Providers;
using Sms.DomainService.Utilities;

namespace XUnitTest.Sms;

public class SmsWebhookVerificationTests
{
    private const string TenantId = "tenant-a";
    private const string AuthToken = "twilio-auth-token";

    private static readonly SmsProviderConfiguration TwilioConfiguration = new()
    {
        ProviderType = SmsProviderType.Twilio,
        AccountId = "AC" + new string('a', 32),
        StatusCallbackBaseUrl = "https://utilities.example.com/"
    };

    private static readonly TwilioSmsProvider Twilio =
        new(Mock.Of<IHttpClientFactory>(), NullLogger<TwilioSmsProvider>.Instance);

    [Fact]
    public void CallbackUrl_MatchesTheWebhookRouteAndCarriesTheTenant()
    {
        SmsCallbackUrls.Build(TwilioConfiguration, TenantId)
            .Should().Be("https://utilities.example.com/sms/twilio/webhooks/tenant-a");
    }

    [Fact]
    public void Twilio_AcceptsACorrectlySignedCallback()
    {
        const string body = "MessageSid=SM123&MessageStatus=delivered&To=%2B41790000000";

        var result = Twilio.VerifyAndParseCallback(Context(), Request(body, Sign(body)));

        result.Verdict.Should().Be(SmsWebhookVerdict.Verified);
        result.Callback!.ProviderMessageId.Should().Be("SM123");
        result.Callback.FinalStatus.Should().Be(SmsRecipientStatus.Delivered);
    }

    [Fact]
    public void Twilio_RejectsATamperedBody()
    {
        var signature = Sign("MessageSid=SM123&MessageStatus=failed");

        var result = Twilio.VerifyAndParseCallback(Context(), Request("MessageSid=SM123&MessageStatus=delivered", signature));

        result.Verdict.Should().Be(SmsWebhookVerdict.Unauthorized);
    }

    [Fact]
    public void Twilio_RejectsASignatureMadeForAnotherTenant()
    {
        const string body = "MessageSid=SM123&MessageStatus=delivered";
        var otherTenantUrl = SmsCallbackUrls.Build(TwilioConfiguration, "tenant-b")!;

        var result = Twilio.VerifyAndParseCallback(Context(), Request(body, Sign(body, otherTenantUrl)));

        result.Verdict.Should().Be(SmsWebhookVerdict.Unauthorized);
    }

    [Fact]
    public void Twilio_RejectsAMissingSignature()
    {
        Twilio.VerifyAndParseCallback(Context(), new SmsWebhookRequest("MessageSid=SM123", new Dictionary<string, string>()))
            .Verdict.Should().Be(SmsWebhookVerdict.Unauthorized);
    }

    [Fact]
    public void Telnyx_RejectsAnUnsignedOrBadlySignedCallback()
    {
        var telnyx = new TelnyxSmsProvider(NullLogger<TelnyxSmsProvider>.Instance);
        var context = new SmsProviderContext(TenantId, new SmsProviderConfiguration
        {
            ProviderType = SmsProviderType.Telnyx,
            WebhookPublicKey = Convert.ToBase64String(new byte[32])
        }, "key");
        const string body = """{"data":{"payload":{"id":"m1","to":[{"status":"delivered"}]}}}""";

        telnyx.VerifyAndParseCallback(context, new SmsWebhookRequest(body, new Dictionary<string, string>()))
            .Verdict.Should().Be(SmsWebhookVerdict.Unauthorized);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [TelnyxSmsProvider.SignatureHeader] = Convert.ToBase64String(new byte[64]),
            [TelnyxSmsProvider.TimestampHeader] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        telnyx.VerifyAndParseCallback(context, new SmsWebhookRequest(body, headers))
            .Verdict.Should().Be(SmsWebhookVerdict.Unauthorized);
    }

    [Theory]
    [InlineData("""{"data":{"payload":{"id":"m1","to":[{"status":"delivered"}]}}}""", SmsRecipientStatus.Delivered)]
    [InlineData("""{"data":{"payload":{"id":"m1","to":[{"status":"delivery_failed"}]}}}""", SmsRecipientStatus.DeliveryFailed)]
    [InlineData("""{"data":{"payload":{"id":"m1","to":[{"status":"sent"}]}}}""", null)]
    public void Telnyx_ParsesTheRecipientStatus(string body, SmsRecipientStatus? expected)
    {
        var result = TelnyxSmsProvider.Parse(body);

        result.Verdict.Should().Be(SmsWebhookVerdict.Verified);
        result.Callback!.FinalStatus.Should().Be(expected);
    }

    [Fact]
    public void Telnyx_TreatsAPayloadWithoutAnIdAsMalformed()
    {
        TelnyxSmsProvider.Parse("""{"data":{}}""").Verdict.Should().Be(SmsWebhookVerdict.Malformed);
        TelnyxSmsProvider.Parse("not json").Verdict.Should().Be(SmsWebhookVerdict.Malformed);
    }

    private static SmsProviderContext Context() => new(TenantId, TwilioConfiguration, AuthToken);

    private static SmsWebhookRequest Request(string body, string signature) =>
        new(body, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [TwilioSmsProvider.SignatureHeader] = signature });

    // Twilio's scheme, computed independently of the SDK: HMAC-SHA1 over the URL followed by every
    // form field as name+value, sorted by name, base64 encoded.
    private static string Sign(string body, string? url = null)
    {
        var form = HttpUtility.ParseQueryString(body);
        var data = new StringBuilder(url ?? SmsCallbackUrls.Build(TwilioConfiguration, TenantId));
        foreach (var key in form.AllKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            data.Append(key).Append(form[key]);
        }

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(AuthToken));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data.ToString())));
    }
}
