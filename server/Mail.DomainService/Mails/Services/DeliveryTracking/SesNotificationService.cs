using System.Text.Json;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails.Services.DeliveryTracking;

public sealed class SesNotificationService : ISesNotificationService
{
    private readonly ILogger<SesNotificationService> _logger;
    private readonly IAmazonSnsMessageVerifier _verifier;
    private readonly IMailRepository _repository;
    private readonly IMailOutboxService _outbox;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public SesNotificationService(
        ILogger<SesNotificationService> logger,
        IAmazonSnsMessageVerifier verifier,
        IMailRepository repository,
        IMailOutboxService outbox,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _verifier = verifier;
        _repository = repository;
        _outbox = outbox;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<SesNotificationResult> ProcessAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        if (!_configuration.GetValue("AmazonSes:DeliveryTrackingEnabled", false))
        {
            return SesNotificationResult.Forbidden("SesDeliveryTrackingDisabled");
        }

        if (!await _verifier.VerifyAsync(payloadJson, cancellationToken))
        {
            return SesNotificationResult.Forbidden("InvalidSnsSignature");
        }

        using var envelopeDocument = JsonDocument.Parse(payloadJson);
        var envelope = envelopeDocument.RootElement;
        var topicArn = GetString(envelope, "TopicArn");
        if (!string.Equals(topicArn, _configuration["AmazonSes:NotificationTopicArn"], StringComparison.Ordinal))
        {
            return SesNotificationResult.Forbidden("UnexpectedSnsTopic");
        }

        var type = GetString(envelope, "Type");
        if (type == "SubscriptionConfirmation")
        {
            return await ConfirmSubscriptionAsync(envelope, cancellationToken);
        }

        if (type == "UnsubscribeConfirmation")
        {
            _logger.LogWarning("Amazon SNS subscription was removed. TopicArn={TopicArn}", topicArn);
            return new SesNotificationResult(SesNotificationOutcome.UnsubscribeAcknowledged);
        }

        if (type != "Notification")
        {
            return SesNotificationResult.Invalid("UnsupportedSnsMessageType");
        }

        return await ProcessNotificationAsync(envelope, cancellationToken);
    }

    private async Task<SesNotificationResult> ProcessNotificationAsync(JsonElement envelope, CancellationToken cancellationToken)
    {
        var snsMessageId = GetString(envelope, "MessageId");
        var messageJson = GetString(envelope, "Message");
        if (string.IsNullOrWhiteSpace(snsMessageId) || string.IsNullOrWhiteSpace(messageJson))
        {
            return SesNotificationResult.Invalid("MissingSnsMessageData");
        }

        using var eventDocument = JsonDocument.Parse(messageJson);
        var sesEvent = eventDocument.RootElement;
        var eventType = GetString(sesEvent, "eventType");
        if (string.IsNullOrWhiteSpace(eventType))
        {
            eventType = GetString(sesEvent, "notificationType");
        }

        if (!TryGetTag(sesEvent, "tenantId", out var tenantId) ||
            !TryGetTag(sesEvent, "mailItemId", out var mailItemId))
        {
            return SesNotificationResult.Invalid("MissingSesCorrelationTags");
        }

        var mail = await _repository.GetMailToBeSent(tenantId, mailItemId);
        if (mail == null || !string.Equals(mail.TenantId, tenantId, StringComparison.Ordinal))
        {
            return SesNotificationResult.Forbidden("SesMailTenantMismatch");
        }

        var claimed = await _repository.TryClaimSesNotificationAsync(
            tenantId,
            snsMessageId,
            mailItemId,
            eventType,
            DateTime.UtcNow);
        if (!claimed)
        {
            return SesNotificationResult.Duplicate();
        }

        try
        {
            var updates = BuildUpdates(sesEvent, eventType, mail);
            foreach (var update in updates)
            {
                var existing = mail.RecipientDeliveryStatuses.FirstOrDefault(x =>
                    string.Equals(x.Recipient, update.Recipient, StringComparison.OrdinalIgnoreCase));
                if (existing != null && !ShouldApplyTransition(existing.Status, update.Status, existing.StatusReason, update.Reason))
                {
                    continue;
                }

                await _repository.UpdateMailRecipientDeliveryStatusAsync(
                    tenantId,
                    mailItemId,
                    update.Recipient,
                    update.Status,
                    update.Reason,
                    update.EventAtUtc);

                await _outbox.EnqueueAsync(
                    mailItemId,
                    CommunicationConstants.MailDeliveryStatusChangedTopicName,
                    new MailDeliveryStatusChangedEvent
                    {
                        ItemId = mailItemId,
                        ProjectKey = mail.ProjectKey,
                        TenantId = tenantId,
                        OrganizationId = mail.OrganizationId,
                        Recipient = update.Recipient,
                        Status = update.Status,
                        StatusReason = update.Reason,
                        CheckedAtUtc = update.EventAtUtc
                    },
                    $"ses-delivery:{snsMessageId}:{update.Recipient}:{update.Status}");
            }

            var sesMessageId = TryGetNestedString(sesEvent, "mail", "messageId");
            await _repository.MarkSesNotificationProcessedAsync(
                tenantId,
                snsMessageId,
                sesMessageId,
                mailItemId,
                DateTime.UtcNow);

            _logger.LogInformation(
                "Processed SES delivery notification. ItemId={ItemId}, EventType={EventType}, TenantId={TenantId}, ProjectKey={ProjectKey}, OrganizationId={OrganizationId}",
                mailItemId,
                eventType,
                tenantId,
                mail.ProjectKey,
                mail.OrganizationId);
            return SesNotificationResult.Processed();
        }
        catch (Exception ex)
        {
            await _repository.ReleaseSesNotificationAsync(tenantId, snsMessageId, ex.GetType().Name);
            throw;
        }
    }

    private async Task<SesNotificationResult> ConfirmSubscriptionAsync(JsonElement envelope, CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue("AmazonSes:AutomaticallyConfirmSubscription", false))
        {
            return new SesNotificationResult(SesNotificationOutcome.SubscriptionConfirmed);
        }

        var subscribeUrl = GetString(envelope, "SubscribeURL");
        if (!Uri.TryCreate(subscribeUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !AmazonSnsMessageVerifier.IsAmazonSnsHost(uri.Host))
        {
            return SesNotificationResult.Forbidden("InvalidSnsSubscribeUrl");
        }

        using var response = await _httpClientFactory.CreateClient().GetAsync(subscribeUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        return new SesNotificationResult(SesNotificationOutcome.SubscriptionConfirmed);
    }

    private static IReadOnlyList<SesRecipientUpdate> BuildUpdates(JsonElement root, string eventType, MailToBeSent mail)
    {
        var status = eventType switch
        {
            "Delivery" => MailStatus.Delivered,
            "Bounce" => MailStatus.Bounced,
            "Reject" => MailStatus.Rejected,
            "Rendering Failure" or "RenderingFailure" => MailStatus.Failed,
            "Complaint" => MailStatus.Complained,
            _ => MailStatus.Pending
        };
        var eventAtUtc = GetEventTimestamp(root, eventType);
        var reason = GetReason(root, eventType);
        var recipients = GetEventRecipients(root, eventType);
        if (recipients.Count == 0)
        {
            recipients = mail.RecipientDeliveryStatuses.Select(x => x.Recipient).ToList();
        }

        return recipients
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => new SesRecipientUpdate(x, status, reason, eventAtUtc))
            .ToList();
    }

    private static bool ShouldApplyTransition(
        MailStatus current,
        MailStatus next,
        string? currentReason,
        string? nextReason)
    {
        if (current == next)
        {
            return !string.Equals(currentReason, nextReason, StringComparison.Ordinal);
        }

        if (next == MailStatus.Pending)
        {
            return current is MailStatus.Pending or MailStatus.Unknown;
        }

        if (current == MailStatus.Complained)
        {
            return false;
        }

        if (next == MailStatus.Complained)
        {
            return true;
        }

        return current is MailStatus.Pending or MailStatus.Unknown;
    }

    private static List<string> GetEventRecipients(JsonElement root, string eventType)
    {
        var result = new List<string>();
        if (eventType == "Bounce" &&
            TryGetNestedArray(root, "bounce", "bouncedRecipients", out var bounced))
        {
            result.AddRange(bounced.EnumerateArray().Select(x => GetString(x, "emailAddress")));
        }
        else if (eventType == "Complaint" &&
                 TryGetNestedArray(root, "complaint", "complainedRecipients", out var complained))
        {
            result.AddRange(complained.EnumerateArray().Select(x => GetString(x, "emailAddress")));
        }
        else if (eventType == "Delivery" &&
                 root.TryGetProperty("delivery", out var delivery) &&
                 delivery.TryGetProperty("recipients", out var delivered) &&
                 delivered.ValueKind == JsonValueKind.Array)
        {
            result.AddRange(delivered.EnumerateArray().Select(x => x.GetString() ?? string.Empty));
        }

        return result;
    }

    private static string? GetReason(JsonElement root, string eventType)
    {
        return eventType switch
        {
            "Bounce" => TryGetNestedString(root, "bounce", "bounceType"),
            "Complaint" => TryGetNestedString(root, "complaint", "complaintFeedbackType"),
            "Reject" => TryGetNestedString(root, "reject", "reason"),
            "Rendering Failure" or "RenderingFailure" => TryGetNestedString(root, "failure", "errorMessage"),
            "DeliveryDelay" => TryGetNestedString(root, "deliveryDelay", "delayType"),
            _ => null
        };
    }

    private static DateTime GetEventTimestamp(JsonElement root, string eventType)
    {
        var section = eventType switch
        {
            "Bounce" => "bounce",
            "Complaint" => "complaint",
            "Delivery" => "delivery",
            "DeliveryDelay" => "deliveryDelay",
            _ => "mail"
        };
        var value = TryGetNestedString(root, section, "timestamp");
        return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var timestamp)
            ? timestamp.ToUniversalTime()
            : DateTime.UtcNow;
    }

    private static bool TryGetTag(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty("mail", out var mail) ||
            !mail.TryGetProperty("tags", out var tags) ||
            !tags.TryGetProperty(name, out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        value = values.EnumerateArray().FirstOrDefault().GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetNestedArray(JsonElement root, string parent, string child, out JsonElement value)
    {
        value = default;
        return root.TryGetProperty(parent, out var parentElement) &&
               parentElement.TryGetProperty(child, out value) &&
               value.ValueKind == JsonValueKind.Array;
    }

    private static string? TryGetNestedString(JsonElement root, string parent, string child)
    {
        return root.TryGetProperty(parent, out var parentElement) &&
               parentElement.TryGetProperty(child, out var childElement)
            ? childElement.GetString()
            : null;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private sealed record SesRecipientUpdate(string Recipient, MailStatus Status, string? Reason, DateTime EventAtUtc);
}
