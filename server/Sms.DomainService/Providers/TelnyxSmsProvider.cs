using Microsoft.Extensions.Logging;
using Sms.DomainService.Dtos;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Utilities;
using Telnyx;

namespace Sms.DomainService.Providers;

public class TelnyxSmsProvider : ISmsProvider
{
    private readonly ILogger<TelnyxSmsProvider> _logger;

    public TelnyxSmsProvider(ILogger<TelnyxSmsProvider> logger)
    {
        _logger = logger;
    }

    public SmsProviderType ProviderType => SmsProviderType.Telnyx;

    public async Task<SmsProviderResult> SendAsync(SmsMessage message, SmsProviderConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            var messageService = new MessageService(configuration.AuthToken);
            OutboundMessage? lastMessage = null;

            foreach (var destinationNumber in message.DestinationNumbers)
            {
                _logger.LogInformation(
                    "TelnyxSmsProvider: sending MessageId={MessageId}, To={Destination}, CorrelationId={CorrelationId}",
                    message.ItemId,
                    SmsLogSanitizer.MaskPhoneNumber(destinationNumber),
                    message.CorrelationId);

                var newMessage = new NewMessage
                {
                    From = configuration.Sender,
                    To = destinationNumber,
                    Text = message.MessageText,
                    WebhookUrl = BuildStatusCallback(configuration)
                };

                if (!string.IsNullOrWhiteSpace(configuration.MessagingProfileId) &&
                    Guid.TryParse(configuration.MessagingProfileId, out var messagingProfileId))
                {
                    newMessage.MessagingProfileId = messagingProfileId;
                }

                lastMessage = await messageService.CreateAsync(newMessage);
            }

            if (lastMessage == null)
            {
                return SmsProviderResult.Failed("telnyx_empty_destination", "No destination numbers were sent.", false);
            }

            if (lastMessage.Errors is { Count: > 0 })
            {
                var firstError = lastMessage.Errors[0];
                return SmsProviderResult.Failed(firstError.Code ?? "telnyx_send_failed", firstError.Title ?? "Telnyx send failed.", false);
            }

            return SmsProviderResult.Submitted(lastMessage.Id?.ToString() ?? string.Empty, "submitted");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TelnyxSmsProvider: send failed for MessageId={MessageId}, CorrelationId={CorrelationId}", message.ItemId, message.CorrelationId);
            return SmsProviderResult.Failed("telnyx_send_failed", SmsLogSanitizer.SanitizeError(ex.Message), IsTransient(ex));
        }
    }

    public Task<SmsProviderDeliveryStatus> GetDeliveryStatusAsync(SmsMessage message, SmsProviderConfiguration configuration, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SmsProviderDeliveryStatus
        {
            IsFinal = false,
            Status = SmsMessageStatus.Submitted,
            ProviderStatus = "submitted"
        });
    }

    private static string? BuildStatusCallback(SmsProviderConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.StatusCallbackBaseUrl))
        {
            return null;
        }

        return $"{configuration.StatusCallbackBaseUrl.TrimEnd('/')}/api/Sms/Webhook/Telnyx";
    }

    private static bool IsTransient(Exception ex)
    {
        return ex is TimeoutException or TaskCanceledException;
    }
}
