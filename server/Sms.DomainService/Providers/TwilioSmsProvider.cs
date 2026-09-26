using System.Web;
using Microsoft.Extensions.Logging;
using Sms.DomainService.Dtos;
using Sms.DomainService.Enums;
using Sms.DomainService.Utilities;
using Twilio.Clients;
using Twilio.Http;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Security;
using Twilio.Types;

namespace Sms.DomainService.Providers;

public class TwilioSmsProvider : ISmsProvider
{
    public const string HttpClientName = "sms-twilio";
    public const string SignatureHeader = "X-Twilio-Signature";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(IHttpClientFactory httpClientFactory, ILogger<TwilioSmsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public SmsProviderType ProviderType => SmsProviderType.Twilio;

    public async Task<SmsProviderResult> SendAsync(SmsProviderContext context, string to, string body, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var callback = SmsCallbackUrls.Build(context.Configuration, context.TenantId);
            var options = new CreateMessageOptions(new PhoneNumber(to))
            {
                From = new PhoneNumber(context.Configuration.ResolveFrom()),
                Body = body,
                StatusCallback = callback == null ? null : new Uri(callback)
            };

            var message = await MessageResource.CreateAsync(options, CreateClient(context));
            return SmsProviderResult.Submitted(message.Sid);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "TwilioSmsProvider: send failed To={Destination}", SmsLogSanitizer.MaskPhoneNumber(to));
            return SmsProviderResult.Failed("twilio_send_failed", SmsLogSanitizer.SanitizeError(ex.Message), SmsTransientErrors.IsTransient(ex));
        }
    }

    public async Task<SmsProviderDeliveryStatus> GetDeliveryStatusAsync(SmsProviderContext context, string providerMessageId, CancellationToken cancellationToken = default)
    {
        var message = await MessageResource.FetchAsync(new FetchMessageOptions(providerMessageId), CreateClient(context));
        return new SmsProviderDeliveryStatus
        {
            FinalStatus = MapStatus(message.Status?.ToString()),
            ErrorCode = message.ErrorCode?.ToString(),
            ErrorMessage = message.ErrorMessage
        };
    }

    public SmsWebhookParseResult VerifyAndParseCallback(SmsProviderContext context, SmsWebhookRequest request)
    {
        // Twilio signs the URL it was given plus the form fields, so validate against the URL we
        // handed it rather than whatever scheme and host the request arrived with behind a proxy.
        var url = SmsCallbackUrls.Build(context.Configuration, context.TenantId);
        if (url == null || !request.Headers.TryGetValue(SignatureHeader, out var signature) || string.IsNullOrWhiteSpace(signature))
        {
            return new SmsWebhookParseResult(SmsWebhookVerdict.Unauthorized);
        }

        var form = HttpUtility.ParseQueryString(request.RawBody);
        if (!new RequestValidator(context.ApiKey).Validate(url, form, signature))
        {
            return new SmsWebhookParseResult(SmsWebhookVerdict.Unauthorized);
        }

        var providerMessageId = form["MessageSid"] ?? form["SmsSid"];
        if (string.IsNullOrWhiteSpace(providerMessageId))
        {
            return new SmsWebhookParseResult(SmsWebhookVerdict.Malformed);
        }

        return new SmsWebhookParseResult(
            SmsWebhookVerdict.Verified,
            new SmsDeliveryCallback(
                providerMessageId,
                MapStatus(form["MessageStatus"] ?? form["SmsStatus"]),
                form["ErrorCode"],
                form["ErrorMessage"]));
    }

    internal static SmsRecipientStatus? MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "delivered" => SmsRecipientStatus.Delivered,
        "undelivered" => SmsRecipientStatus.Undelivered,
        "failed" => SmsRecipientStatus.DeliveryFailed,
        _ => null
    };

    // Per tenant, per call: the SDK's static TwilioClient.Init is process-wide, so two tenants
    // sending at once would race on whose credentials go out. The HttpClient is pooled by the factory.
    private TwilioRestClient CreateClient(SmsProviderContext context) =>
        new(
            username: context.Configuration.AccountId,
            password: context.ApiKey,
            httpClient: new SystemNetHttpClient(_httpClientFactory.CreateClient(HttpClientName)));
}
