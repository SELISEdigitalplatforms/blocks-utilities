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
/// Converts a seat, plan or cancellation lifecycle event into the mail command understood by
/// Blocks OS, addressed to the subscription's billing contact.
/// </summary>
/// <remarks>
/// Every purpose is sent the same full set of keys, empty where a value does not apply. The mail
/// module rejects a template whose placeholder has no key — silently, after acknowledging the
/// message — so a tenant who words "cancelled by {{ActorName}}" into a template must never meet an
/// event that happens to leave the actor out.
/// </remarks>
public sealed class SubscriptionNotificationEmailService : ISubscriptionNotificationEmailService
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly IMessageClient _messageClient;
    private readonly ILogger<SubscriptionNotificationEmailService> _logger;
    private readonly IMailDeliveryReporter? _mailReports;

    public SubscriptionNotificationEmailService(
        ISubscriptionRepository subscriptions,
        IBillingAccountRepository billingAccounts,
        IMessageClient messageClient,
        ILogger<SubscriptionNotificationEmailService> logger,
        IMailDeliveryReporter? mailReports = null)
    {
        _subscriptions = subscriptions;
        _billingAccounts = billingAccounts;
        _messageClient = messageClient;
        _logger = logger;
        // Optional for the same reason as on the usage-threshold path: absent, nothing is recorded.
        _mailReports = mailReports;
    }

    public async Task SendAsync(
        SubscriptionLifecycleEvent lifecycleEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lifecycleEvent);

        if (!SubscriptionConstants.NotificationMailPurposes.TryGetValue(
                lifecycleEvent.EventType, out var purpose))
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
                "Subscription notification email skipped because the subscription was not found " +
                "TenantId={TenantId} SubscriptionId={SubscriptionId} EventId={EventId}",
                PaymentLogValue.Id(lifecycleEvent.TenantId),
                PaymentLogValue.Id(lifecycleEvent.SubscriptionId),
                lifecycleEvent.EventId);
            await ReportNotAttemptedAsync(lifecycleEvent, "subscription_not_found", cancellationToken);
            return;
        }

        var account = await _billingAccounts.GetAsync(
            lifecycleEvent.TenantId,
            subscription.BillingAccountId,
            cancellationToken);

        if (account is null || string.IsNullOrWhiteSpace(account.BillingEmail))
        {
            _logger.LogWarning(
                "Subscription notification email skipped because the billing account has no " +
                "recipient TenantId={TenantId} SubscriptionId={SubscriptionId} EventId={EventId}",
                PaymentLogValue.Id(lifecycleEvent.TenantId),
                PaymentLogValue.Id(lifecycleEvent.SubscriptionId),
                lifecycleEvent.EventId);
            await ReportNotAttemptedAsync(lifecycleEvent, "billing_email_missing", cancellationToken);
            return;
        }

        var body = BodyContext(
            lifecycleEvent,
            string.IsNullOrWhiteSpace(account.BillingName) ? account.BillingEmail : account.BillingName,
            subscription.FeeSchedule.TimeZoneId);

        // The reason is the canceller's own words. The mail module encodes body values but places
        // subject values raw, so it stays out of the subject however a tenant words the template.
        var subject = new Dictionary<string, string>(body);
        subject.Remove(CancellationReasonKey);

        var payload = new SendMail
        {
            To = [account.BillingEmail.Trim().ToLowerInvariant()],
            Purpose = purpose,
            Language = SubscriptionConstants.DefaultMailLanguage,
            SubjectDataContext = subject,
            BodyDataContext = body,
            CorrelationId = lifecycleEvent.EventId
        };

        await _messageClient.SendToConsumerAsync(
            new ConsumerMessage<SendMail>
            {
                ConsumerName = SubscriptionConstants.MailQueue,
                Payload = payload
            });

        // After the send, and only on success, for the reason the usage-threshold path gives: a
        // throw above is retried with the whole event, and a row here would describe a mail that
        // is about to be sent again.
        if (_mailReports is not null)
        {
            await _mailReports.RecordAsync(
                Report(lifecycleEvent, MailDeliveryReportOutcome.Published, payload, null),
                cancellationToken);
        }

        _logger.LogInformation(
            "Subscription notification email queued TenantId={TenantId} " +
            "SubscriptionId={SubscriptionId} Purpose={Purpose} EventId={EventId}",
            PaymentLogValue.Id(lifecycleEvent.TenantId),
            PaymentLogValue.Id(lifecycleEvent.SubscriptionId),
            PaymentLogValue.Label(purpose),
            lifecycleEvent.EventId);
    }

    private const string CancellationReasonKey = "CancellationReason";

    /// <summary>The keys every billing-contact purpose receives.</summary>
    private static Dictionary<string, string> BodyContext(
        SubscriptionLifecycleEvent lifecycleEvent,
        string displayName,
        string? timeZoneId) => new()
    {
        ["DisplayName"] = displayName,
        ["PlanName"] = lifecycleEvent.PlanName ?? string.Empty,
        ["PlanCode"] = lifecycleEvent.PlanCode,
        ["PreviousPlanName"] = lifecycleEvent.PreviousPlanName ?? string.Empty,
        ["ActorName"] = lifecycleEvent.ActorName ?? string.Empty,
        ["QuantityChanges"] = Describe(lifecycleEvent.QuantityChanges),
        ["EffectiveDate"] = EffectiveDate(EffectiveAtUtc(lifecycleEvent), timeZoneId),
        [CancellationReasonKey] = lifecycleEvent.CancellationReason ?? string.Empty
    };

    /// <summary>
    /// The instant the email's news takes hold: when a change was applied, or when access ends.
    /// </summary>
    /// <remarks>
    /// A cancellation event already carries that boundary in <c>CurrentPeriodEndUtc</c>; a
    /// withdrawal carries none, because nothing is ending any more.
    /// </remarks>
    private static DateTime? EffectiveAtUtc(SubscriptionLifecycleEvent lifecycleEvent) =>
        lifecycleEvent.EventType switch
        {
            SubscriptionConstants.SubscriptionCancellationRequested or
            SubscriptionConstants.SubscriptionCanceled => lifecycleEvent.CurrentPeriodEndUtc,
            SubscriptionConstants.SubscriptionCancellationWithdrawn => null,
            _ => lifecycleEvent.OccurredAtUtc
        };

    /// <summary>
    /// One line per changed item, as <c>UnitLabel: old → new</c>.
    /// </summary>
    /// <remarks>
    /// Joined on a separator rather than line breaks: the mail module HTML-encodes body values, so a
    /// <c>&lt;br&gt;</c> would arrive as text, and a bare newline collapses in an HTML body.
    /// </remarks>
    private static string Describe(List<LifecycleQuantityChange>? changes) =>
        changes is null or { Count: 0 }
            ? string.Empty
            : string.Join("; ", changes.Select(change => string.Create(
                CultureInfo.InvariantCulture,
                $"{(string.IsNullOrWhiteSpace(change.UnitLabel) ? change.ItemKey : change.UnitLabel)}: {change.PreviousQuantity} → {change.Quantity}")));

    /// <summary>
    /// How the email states the effective instant: the date it falls on in the subscriber's own
    /// billing timezone, as <c>yyyy-MM-dd</c>.
    /// </summary>
    /// <remarks>
    /// Local, because billing boundaries are local midnights: access ending at the start of
    /// 1 November in Zurich is stored as 23:00 UTC on 31 October, and a UTC date would tell the
    /// customer they lose access a day early. ISO rather than a written-out month, because the mail
    /// language is not yet the recipient's and a numeric date reads the same in every one of them.
    /// A zone that cannot be resolved falls back to UTC rather than failing the mail.
    /// </remarks>
    private static string EffectiveDate(DateTime? instantUtc, string? timeZoneId)
    {
        if (instantUtc is not { } instant)
        {
            return string.Empty;
        }

        BillingLocalTime.TryFindTimeZone(timeZoneId, out var zone);

        return TimeZoneInfo
            .ConvertTimeFromUtc(DateTime.SpecifyKind(instant, DateTimeKind.Utc), zone)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private async Task ReportNotAttemptedAsync(
        SubscriptionLifecycleEvent lifecycleEvent,
        string errorCode,
        CancellationToken cancellationToken)
    {
        if (_mailReports is null)
        {
            return;
        }

        await _mailReports.RecordAsync(
            Report(lifecycleEvent, MailDeliveryReportOutcome.NotAttempted, null, errorCode),
            cancellationToken);
    }

    private static MailDeliveryReportRequest Report(
        SubscriptionLifecycleEvent lifecycleEvent,
        MailDeliveryReportOutcome outcome,
        SendMail? payload,
        string? errorCode) => new()
    {
        TenantId = lifecycleEvent.TenantId,
        OrganizationId = lifecycleEvent.OrganizationId,
        Source = MailDeliveryReportSource.SubscriptionNotification,
        Outcome = outcome,
        SubjectId = lifecycleEvent.SubscriptionId,
        SubjectReference = lifecycleEvent.EventId,
        // Not claimed: a redelivered event may send again, which the spec accepts.
        MailMessageId = null,
        ConsumerName = SubscriptionConstants.MailQueue,
        Payload = payload,
        ErrorCode = errorCode,
        CorrelationId = lifecycleEvent.EventId
    };
}
