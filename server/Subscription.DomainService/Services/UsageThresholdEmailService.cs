using System.Globalization;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Messaging;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Converts a usage-threshold lifecycle event into the mail command understood by Blocks OS.
/// </summary>
public sealed class UsageThresholdEmailService : IUsageThresholdEmailService
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly IMessageClient _messageClient;
    private readonly ILogger<UsageThresholdEmailService> _logger;
    private readonly IMailDeliveryReporter? _mailReports;

    public UsageThresholdEmailService(
        ISubscriptionRepository subscriptions,
        IBillingAccountRepository billingAccounts,
        IMessageClient messageClient,
        ILogger<UsageThresholdEmailService> logger,
        IMailDeliveryReporter? mailReports = null)
    {
        _subscriptions = subscriptions;
        _billingAccounts = billingAccounts;
        _messageClient = messageClient;
        _logger = logger;
        // Optional for the same reason as on the document path: absent, nothing is recorded and
        // the mail behaves exactly as it did before.
        _mailReports = mailReports;
    }

    public async Task SendAsync(
        SubscriptionLifecycleEvent lifecycleEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lifecycleEvent);

        if (lifecycleEvent.EventType != SubscriptionConstants.UsageThresholdReached)
        {
            return;
        }

        var subscription = await _subscriptions.GetByIdAsync(
            lifecycleEvent.TenantId,
            lifecycleEvent.SubscriptionId,
            cancellationToken);

        if (subscription is null)
        {
            _logger.LogWarning(
                "Usage threshold email skipped because the subscription was not found " +
                "TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} EventId={EventId}",
                PaymentLogValue.Hash(lifecycleEvent.TenantId),
                PaymentLogValue.Hash(lifecycleEvent.SubscriptionId),
                lifecycleEvent.EventId);
            return;
        }

        var account = await _billingAccounts.GetAsync(
            lifecycleEvent.TenantId,
            subscription.BillingAccountId,
            cancellationToken);

        if (account is null || string.IsNullOrWhiteSpace(account.BillingEmail))
        {
            _logger.LogWarning(
                "Usage threshold email skipped because the billing account has no recipient " +
                "TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} EventId={EventId}",
                PaymentLogValue.Hash(lifecycleEvent.TenantId),
                PaymentLogValue.Hash(lifecycleEvent.SubscriptionId),
                lifecycleEvent.EventId);
            return;
        }

        var threshold = Number(lifecycleEvent.ThresholdPercent);
        var balance = Number(lifecycleEvent.Balance);
        var limit = Number(lifecycleEvent.Limit);
        var displayName = string.IsNullOrWhiteSpace(account.BillingName)
            ? account.BillingEmail
            : account.BillingName;

        var context = new Dictionary<string, string>
        {
            ["DisplayName"] = displayName,
            ["PlanName"] = subscription.Plan.DisplayName,
            ["PlanCode"] = subscription.Plan.Code,
            ["MeterKey"] = lifecycleEvent.MeterKey ?? string.Empty,
            ["ThresholdPercent"] = threshold,
            ["Balance"] = balance,
            ["Limit"] = limit
        };

        var payload = new SendMail
        {
            To = [account.BillingEmail.Trim().ToLowerInvariant()],
            Purpose = SubscriptionConstants.UsageThresholdMailPurpose,
            Language = SubscriptionConstants.DefaultMailLanguage,
            SubjectDataContext = new Dictionary<string, string>(context),
            BodyDataContext = context
        };

        await _messageClient.SendToConsumerAsync(
            new ConsumerMessage<SendMail>
            {
                ConsumerName = SubscriptionConstants.MailQueue,
                Payload = payload
            });

        // Recorded after the send, and only on success. A throw above belongs to the caller, which
        // retries the whole lifecycle event -- recording an attempt here would put a row in the
        // history for a mail that is about to be sent again.
        if (_mailReports is not null)
        {
            await _mailReports.RecordAsync(
                new MailDeliveryReportRequest
                {
                    TenantId = lifecycleEvent.TenantId,
                    OrganizationId = subscription.OrganizationId,
                    Source = MailDeliveryReportSource.UsageThreshold,
                    Outcome = MailDeliveryReportOutcome.Published,
                    SubjectId = lifecycleEvent.SubscriptionId,
                    SubjectReference = lifecycleEvent.EventId,
                    // No message id: usage warnings are not claimed and the same threshold may
                    // legitimately warn more than once.
                    MailMessageId = null,
                    ConsumerName = SubscriptionConstants.MailQueue,
                    Payload = payload,
                    CorrelationId = lifecycleEvent.EventId
                },
                cancellationToken);
        }

        _logger.LogInformation(
            "Usage threshold email queued TenantHash={TenantHash} " +
            "SubscriptionHash={SubscriptionHash} ThresholdPercent={ThresholdPercent} " +
            "EventId={EventId}",
            PaymentLogValue.Hash(lifecycleEvent.TenantId),
            PaymentLogValue.Hash(lifecycleEvent.SubscriptionId),
            lifecycleEvent.ThresholdPercent,
            lifecycleEvent.EventId);
    }

    private static string Number(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}
