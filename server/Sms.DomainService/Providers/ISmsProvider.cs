using Sms.DomainService.Dtos;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Providers;

public interface ISmsProvider
{
    SmsProviderType ProviderType { get; }

    /// <summary>
    /// Sends to one recipient from <paramref name="from"/>, already chosen for that destination. Never throws for a provider failure; the result says why.
    /// <paramref name="idempotencyKey"/> is stable per message and recipient, so a provider that
    /// honours it (Telnyx) drops a resend after a worker crash.
    /// </summary>
    Task<SmsProviderResult> SendAsync(SmsProviderContext context, string from, string to, string body, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<SmsProviderDeliveryStatus> GetDeliveryStatusAsync(SmsProviderContext context, string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>Checks the provider's signature before reading anything from the callback.</summary>
    SmsWebhookParseResult VerifyAndParseCallback(SmsProviderContext context, SmsWebhookRequest request);
}
