using Microsoft.Extensions.Logging;
using Sms.DomainService.Dtos;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Utilities;
using System.Text.RegularExpressions;
using Twilio;
using Twilio.Exceptions;
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
            var sender = ResolveSender(configuration);
            if (!sender.IsValid)
            {
                _logger.LogWarning(
                    "TwilioSmsProvider: invalid sender configuration for MessageId={MessageId}, CorrelationId={CorrelationId}, ErrorCode={ErrorCode}",
                    message.ItemId,
                    message.CorrelationId,
                    sender.ErrorCode);

                return SmsProviderResult.Failed(sender.ErrorCode, sender.ErrorMessage, false);
            }

            TwilioClient.Init(configuration.AccountId, configuration.AuthToken);
            MessageResource? lastMessage = null;

            foreach (var destinationNumber in message.DestinationNumbers)
            {
                _logger.LogInformation(
                    "TwilioSmsProvider: sending MessageId={MessageId}, To={Destination}, CorrelationId={CorrelationId}",
                    message.ItemId,
                    SmsLogSanitizer.MaskPhoneNumber(destinationNumber),
                    message.CorrelationId);

                lastMessage = sender.UseMessagingService
                    ? await MessageResource.CreateAsync(
                        to: new PhoneNumber(destinationNumber),
                        messagingServiceSid: sender.MessagingServiceSid,
                        body: message.MessageText,
                        statusCallback: BuildStatusCallback(configuration))
                    : await MessageResource.CreateAsync(
                        to: new PhoneNumber(destinationNumber),
                        from: new PhoneNumber(sender.From),
                        body: message.MessageText,
                        statusCallback: BuildStatusCallback(configuration));
            }

            if (lastMessage == null)
            {
                return SmsProviderResult.Failed("twilio_empty_destination", "No destination numbers were sent.", false);
            }

            return SmsProviderResult.Submitted(lastMessage.Sid, lastMessage.Status?.ToString());
        }
        catch (ApiException ex) when (IsInvalidSenderException(ex))
        {
            _logger.LogError(
                ex,
                "TwilioSmsProvider: invalid sender rejected by Twilio for MessageId={MessageId}, CorrelationId={CorrelationId}, TwilioCode={TwilioCode}",
                message.ItemId,
                message.CorrelationId,
                ex.Code);

            return SmsProviderResult.Failed(
                "twilio_invalid_sender",
                "Twilio rejected the configured sender. Use a Twilio phone number in E.164 format, a numeric short code, a supported alpha sender ID up to 11 characters, or configure MessagingProfileId with a Messaging Service SID that starts with MG.",
                false);
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

    private static TwilioSenderResolution ResolveSender(SmsProviderConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.MessagingProfileId) &&
            configuration.MessagingProfileId.Trim().StartsWith("MG", StringComparison.OrdinalIgnoreCase))
        {
            return TwilioSenderResolution.ForMessagingService(configuration.MessagingProfileId.Trim());
        }

        var sender = configuration.Sender?.Trim();
        if (string.IsNullOrWhiteSpace(sender))
        {
            return TwilioSenderResolution.Invalid("twilio_sender_required", "Twilio sender is required when MessagingProfileId is not a Messaging Service SID.");
        }

        if (IsE164PhoneNumber(sender) || IsNumericShortCode(sender) || IsAlphaSenderId(sender))
        {
            return TwilioSenderResolution.ForFrom(sender);
        }

        return TwilioSenderResolution.Invalid(
            "twilio_invalid_sender",
            "Twilio sender must be an E.164 phone number, numeric short code, or supported alpha sender ID up to 11 letters, numbers, or spaces. For registered sender pools, configure MessagingProfileId with a Twilio Messaging Service SID that starts with MG.");
    }

    private static bool IsE164PhoneNumber(string sender)
    {
        return Regex.IsMatch(sender, @"^\+[1-9]\d{1,14}$", RegexOptions.CultureInvariant);
    }

    private static bool IsNumericShortCode(string sender)
    {
        return Regex.IsMatch(sender, @"^\d{3,10}$", RegexOptions.CultureInvariant);
    }

    private static bool IsAlphaSenderId(string sender)
    {
        return Regex.IsMatch(sender, @"^(?=.{1,11}$)(?=.*[A-Za-z])[A-Za-z0-9 ]+$", RegexOptions.CultureInvariant);
    }

    private static bool IsInvalidSenderException(ApiException ex)
    {
        return ex.Code == 21212 || ex.Message.Contains("Invalid From", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransient(Exception ex)
    {
        return ex is TimeoutException or TaskCanceledException;
    }

    private sealed record TwilioSenderResolution(
        bool IsValid,
        bool UseMessagingService,
        string? From,
        string? MessagingServiceSid,
        string ErrorCode,
        string ErrorMessage)
    {
        public static TwilioSenderResolution ForFrom(string from)
        {
            return new TwilioSenderResolution(true, false, from, null, string.Empty, string.Empty);
        }

        public static TwilioSenderResolution ForMessagingService(string messagingServiceSid)
        {
            return new TwilioSenderResolution(true, true, null, messagingServiceSid, string.Empty, string.Empty);
        }

        public static TwilioSenderResolution Invalid(string errorCode, string errorMessage)
        {
            return new TwilioSenderResolution(false, false, null, null, errorCode, errorMessage);
        }
    }
}
