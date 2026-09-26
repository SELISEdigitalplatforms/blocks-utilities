using Sms.DomainService.Entities;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Utilities;

/// <summary>
/// The one place the webhook URL is spelled. The Api route is <c>sms/{provider}/webhooks/{tenantId}</c>,
/// outside the global <c>api</c> prefix like the payment webhooks, and Twilio's signature covers this
/// exact string, so building it anywhere else is how the two drift apart.
/// </summary>
public static class SmsCallbackUrls
{
    public static string ProviderSegment(SmsProviderType providerType) => providerType.ToString().ToLowerInvariant();

    public static string? Build(SmsProviderConfiguration configuration, string tenantId)
    {
        if (string.IsNullOrWhiteSpace(configuration.StatusCallbackBaseUrl))
        {
            return null;
        }

        return $"{configuration.StatusCallbackBaseUrl.TrimEnd('/')}/sms/{ProviderSegment(configuration.ProviderType)}/webhooks/{Uri.EscapeDataString(tenantId)}";
    }
}
