using Microsoft.Extensions.Logging;
using Sms.DomainService.Dtos;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Utilities;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Sms.DomainService.Providers;

public class TwilioSmsProvider : ISmsProvider
{
    private readonly ILogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(ILogger<TwilioSmsProvider> logger)
    {
        _logger = logger;
    }

    public SmsProviderType ProviderType => SmsProviderType.Twilio;

    public async Task<SmsProviderResult> SendAsync(SmsMessage message, SmsProviderConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            TwilioClient.Init(configuration.AccountId, configuration.AuthToken);
            MessageResource? lastMessage = null;

            foreach (var destinationNumber in message.DestinationNumbers)
            {
                _logger.LogInformation(
                    "TwilioSmsProvider: sending MessageId={MessageId}, To={Destination}, CorrelationId={CorrelationId}",
                    message.ItemId,
                    SmsLogSanitizer.MaskPhoneNumber(destinationNumber),
                    message.CorrelationId);

                lastMessage = await MessageResource.CreateAsync(
                    to: new PhoneNumber(destinationNumber),
                    from: new PhoneNumber(configuration.Sender),
                    body: message.MessageText,
                    statusCallback: BuildStatusCallback(configuration));
            }

            if (lastMessage == null)
            {
                return SmsProviderResult.Failed("twilio_empty_destination", "No destination numbers were sent.", false);
            }

            return SmsProviderResult.Submitted(lastMessage.Sid, lastMessage.Status?.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TwilioSmsProvider: send failed for MessageId={MessageId}, CorrelationId={CorrelationId}", message.ItemId, message.CorrelationId);
            return SmsProviderResult.Failed("twilio_send_failed", SmsLogSanitizer.SanitizeError(ex.Message), IsTransient(ex));
        }
    }

    public async Task<SmsProviderDeliveryStatus> GetDeliveryStatusAsync(SmsMessage message, SmsProviderConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.ProviderMessageId))
        {
            return new SmsProviderDeliveryStatus { IsFinal = false, Status = SmsMessageStatus.Submitted };
        }

        try
        {
            TwilioClient.Init(configuration.AccountId, configuration.AuthToken);
            var providerMessage = await MessageResource.FetchAsync(pathSid: message.ProviderMessageId);
            var status = providerMessage.Status?.ToString()?.ToLowerInvariant();
            return new SmsProviderDeliveryStatus
            {
                IsFinal = status is "delivered" or "undelivered" or "failed",
                Status = status switch
                {
                    "delivered" => SmsMessageStatus.Delivered,
                    "undelivered" => SmsMessageStatus.Undelivered,
                    "failed" => SmsMessageStatus.DeliveryFailed,
                    _ => SmsMessageStatus.Submitted
                },
                ProviderStatus = status,
                ErrorCode = providerMessage.ErrorCode?.ToString(),
                ErrorMessage = providerMessage.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TwilioSmsProvider: delivery check failed for MessageId={MessageId}", message.ItemId);
            return new SmsProviderDeliveryStatus
            {
                IsFinal = false,
                Status = SmsMessageStatus.Submitted,
                ErrorCode = "twilio_delivery_check_failed",
                ErrorMessage = SmsLogSanitizer.SanitizeError(ex.Message)
            };
        }
    }

    private static Uri? BuildStatusCallback(SmsProviderConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.StatusCallbackBaseUrl))
        {
            return null;
        }

        return new Uri($"{configuration.StatusCallbackBaseUrl.TrimEnd('/')}/api/Sms/Webhook/Twilio");
    }

    private static bool IsTransient(Exception ex)
    {
        return ex is TimeoutException or TaskCanceledException;
    }
}
