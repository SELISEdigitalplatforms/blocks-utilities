using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sms.DomainService.Dtos;
using Sms.DomainService.Enums;
using Sms.DomainService.Utilities;
using Telnyx;
using TelnyxWebhook = Telnyx.net.Infrastructure.Public.Webhook;

namespace Sms.DomainService.Providers;

public class TelnyxSmsProvider : ISmsProvider
{
    public const string SignatureHeader = "telnyx-signature-ed25519";
    public const string TimestampHeader = "telnyx-timestamp";
    private const long SignatureToleranceSeconds = 300;

    private readonly ILogger<TelnyxSmsProvider> _logger;

    public TelnyxSmsProvider(ILogger<TelnyxSmsProvider> logger)
    {
        _logger = logger;
    }

    public SmsProviderType ProviderType => SmsProviderType.Telnyx;

    public async Task<SmsProviderResult> SendAsync(SmsProviderContext context, string to, string body, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var newMessage = new NewMessage
            {
                From = context.Configuration.ResolveFrom(),
                To = to,
                Text = body,
                WebhookUrl = SmsCallbackUrls.Build(context.Configuration, context.TenantId)
            };

            if (Guid.TryParse(context.Configuration.MessagingProfileId, out var messagingProfileId))
            {
                newMessage.MessagingProfileId = messagingProfileId;
            }

            var message = await new MessageService(context.ApiKey).CreateAsync(newMessage, new RequestOptions { ApiKey = context.ApiKey, IdempotencyKey = idempotencyKey }, cancellationToken);
            if (message.Errors is { Count: > 0 })
            {
                var error = message.Errors[0];
                return SmsProviderResult.Failed(error.Code ?? "telnyx_send_failed", error.Title ?? "Telnyx send failed.", false);
            }

            return SmsProviderResult.Submitted(message.Id?.ToString() ?? string.Empty);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "TelnyxSmsProvider: send failed To={Destination}", SmsLogSanitizer.MaskPhoneNumber(to));
            return SmsProviderResult.Failed("telnyx_send_failed", SmsLogSanitizer.SanitizeError(ex.Message), SmsTransientErrors.IsTransient(ex));
        }
    }

    public async Task<SmsProviderDeliveryStatus> GetDeliveryStatusAsync(SmsProviderContext context, string providerMessageId, CancellationToken cancellationToken = default)
    {
        var message = await new MessageService(context.ApiKey).GetAsync(providerMessageId, Options(context), cancellationToken);
        var status = message.To?.FirstOrDefault()?.Status;
        return new SmsProviderDeliveryStatus
        {
            FinalStatus = status switch
            {
                OutboundMessageTo.StatusEnum.DeliveredEnum => SmsRecipientStatus.Delivered,
                OutboundMessageTo.StatusEnum.DeliveryFailedEnum or OutboundMessageTo.StatusEnum.SendingFailedEnum => SmsRecipientStatus.DeliveryFailed,
                OutboundMessageTo.StatusEnum.DeliveryUnconfirmedEnum => SmsRecipientStatus.Undelivered,
                _ => null
            },
            ErrorCode = message.Errors?.FirstOrDefault()?.Code,
            ErrorMessage = message.Errors?.FirstOrDefault()?.Title
        };
    }

    public SmsWebhookParseResult VerifyAndParseCallback(SmsProviderContext context, SmsWebhookRequest request)
    {
        if (string.IsNullOrWhiteSpace(context.Configuration.WebhookPublicKey) ||
            !request.Headers.TryGetValue(SignatureHeader, out var signature) ||
            !request.Headers.TryGetValue(TimestampHeader, out var timestamp))
        {
            return new SmsWebhookParseResult(SmsWebhookVerdict.Unauthorized);
        }

        try
        {
            // Ed25519 over "{timestamp}|{body}" with the account public key, and a freshness window.
            TelnyxWebhook.ConstructEvent(request.RawBody, signature, timestamp, context.Configuration.WebhookPublicKey, SignatureToleranceSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("TelnyxSmsProvider: webhook signature rejected Reason={Reason}", ex.GetType().Name);
            return new SmsWebhookParseResult(SmsWebhookVerdict.Unauthorized);
        }

        return Parse(request.RawBody);
    }

    public static SmsWebhookParseResult Parse(string rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("payload", out var payload) ||
                !payload.TryGetProperty("id", out var id) ||
                string.IsNullOrWhiteSpace(id.GetString()))
            {
                return new SmsWebhookParseResult(SmsWebhookVerdict.Malformed);
            }

            string? status = null;
            if (payload.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.Array && to.GetArrayLength() > 0 &&
                to[0].TryGetProperty("status", out var statusElement))
            {
                status = statusElement.GetString();
            }

            string? errorCode = null;
            string? errorTitle = null;
            if (payload.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                errorCode = errors[0].TryGetProperty("code", out var code) ? code.ToString() : null;
                errorTitle = errors[0].TryGetProperty("title", out var title) ? title.GetString() : null;
            }

            return new SmsWebhookParseResult(
                SmsWebhookVerdict.Verified,
                new SmsDeliveryCallback(id.GetString()!, MapStatus(status), errorCode, errorTitle));
        }
        catch (JsonException)
        {
            return new SmsWebhookParseResult(SmsWebhookVerdict.Malformed);
        }
    }

    internal static SmsRecipientStatus? MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "delivered" => SmsRecipientStatus.Delivered,
        "delivery_failed" or "sending_failed" => SmsRecipientStatus.DeliveryFailed,
        "delivery_unconfirmed" => SmsRecipientStatus.Undelivered,
        _ => null
    };

    private static RequestOptions Options(SmsProviderContext context) => new() { ApiKey = context.ApiKey };
}
