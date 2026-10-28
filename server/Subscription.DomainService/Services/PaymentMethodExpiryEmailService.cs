using System.Globalization;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Messaging;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Finds saved cards whose expiry month ends within the notice window and mails the billing
/// contact of the account that charges each one, once per expiry.
/// </summary>
/// <remarks>
/// Driven by a sweep rather than a lifecycle event, because nothing happens on the day a card
/// starts approaching expiry — the only thing that changes is the date. The card itself records
/// which expiry was announced (<c>ExpiryNoticeSentForExpiry</c>), so a crash between the send and
/// that write costs at most one duplicate mail, never a missed one.
/// <para>
/// A card no billing account charges is marked without a mail: nobody is renewing off it, so there
/// is no one to warn, and leaving it unmarked would return it to every pass ahead of cards that do
/// matter.
/// </para>
/// </remarks>
public sealed class PaymentMethodExpiryEmailService : IPaymentMethodExpiryEmailService
{
    private readonly IStoredPaymentMethodRepository _storedMethods;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly IMessageClient _messageClient;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<PaymentMethodExpiryEmailService> _logger;
    private readonly TimeProvider _time;
    private readonly IMailDeliveryReporter? _mailReports;
    private readonly ISubscriptionBillingProfileRepository? _profiles;

    public PaymentMethodExpiryEmailService(
        IStoredPaymentMethodRepository storedMethods,
        IBillingAccountRepository billingAccounts,
        IMessageClient messageClient,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<PaymentMethodExpiryEmailService> logger,
        TimeProvider? time = null,
        // Optional for the same reason as on the other mail paths: absent, nothing is recorded.
        IMailDeliveryReporter? mailReports = null,
        // Optional: absent, the billing contact is mailed in the account's language, else en-US.
        ISubscriptionBillingProfileRepository? profiles = null)
    {
        _storedMethods = storedMethods;
        _billingAccounts = billingAccounts;
        _messageClient = messageClient;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _mailReports = mailReports;
        _profiles = profiles;
    }

    public async Task<int> NotifyAsync(string tenantId, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;

        var methods = await _storedMethods.ListExpiringAsync(
            tenantId,
            CardExpiry.EndingWithin(now, Math.Max(1, options.PaymentMethodExpiryNoticeDays)),
            Math.Max(1, options.PaymentMethodExpiryNoticeBatchSize),
            cancellationToken);

        var sent = 0;

        foreach (var method in methods)
        {
            if (!CardExpiry.TryParse(method.ExpiryMonth, method.ExpiryYear, out var expiry))
            {
                // The filter matched it on the same strings, so this is a shape the parser does
                // not know. Left alone rather than marked, so a fix to the parser picks it up.
                _logger.LogWarning(
                    "Card expiry could not be read for an expiry notice TenantId={TenantId} " +
                    "PaymentMethodId={PaymentMethodId}",
                    PaymentLogValue.Id(tenantId),
                    PaymentLogValue.Id(method.ItemId));
                continue;
            }

            var account = await _billingAccounts.FindByDefaultPaymentMethodAsync(
                tenantId, method.ItemId, cancellationToken);

            if (account is null || string.IsNullOrWhiteSpace(account.BillingEmail))
            {
                _logger.LogInformation(
                    "Expiring card has no billing contact to tell; marked without a mail " +
                    "TenantId={TenantId} PaymentMethodId={PaymentMethodId} Expiry={Expiry}",
                    PaymentLogValue.Id(tenantId),
                    PaymentLogValue.Id(method.ItemId),
                    PaymentLogValue.Label(expiry.Key));

                await _storedMethods.MarkExpiryNoticeSentAsync(
                    tenantId, method.ItemId, expiry.Key, cancellationToken);
                continue;
            }

            var context = new Dictionary<string, string>
            {
                ["DisplayName"] = string.IsNullOrWhiteSpace(account.BillingName)
                    ? account.BillingEmail
                    : account.BillingName,
                ["Brand"] = method.Brand ?? string.Empty,
                ["LastFour"] = method.LastFour ?? string.Empty,
                ["ExpiryMonth"] = expiry.Month.ToString("00", CultureInfo.InvariantCulture),
                ["ExpiryYear"] = expiry.Year.ToString("0000", CultureInfo.InvariantCulture),
                ["ExpiryDate"] = expiry.LastDayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };

            // Read now rather than copied earlier, for the reason the notification service gives:
            // the profile is the one place an administrator can change the language.
            var profile = _profiles is null
                ? null
                : await _profiles.GetAsync(tenantId, account.OrganizationId, cancellationToken);

            // Deterministic, so a duplicate send after a crash is recognisable as one in the mail
            // history rather than looking like a second expiry.
            var correlationId = $"card-expiry-{method.ItemId}-{expiry.Key}";

            var payload = new SendMail
            {
                To = [account.BillingEmail.Trim().ToLowerInvariant()],
                Purpose = SubscriptionConstants.PaymentMethodExpiringMailPurpose,
                Language = MailLanguage.FirstOf(profile?.BillingContactLanguage, account.Language),
                SubjectDataContext = new Dictionary<string, string>(context),
                BodyDataContext = context,
                CorrelationId = correlationId
            };

            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<SendMail>
                {
                    ConsumerName = SubscriptionConstants.MailQueue,
                    Payload = payload
                });

            if (_mailReports is not null)
            {
                await _mailReports.RecordAsync(
                    new MailDeliveryReportRequest
                    {
                        TenantId = tenantId,
                        OrganizationId = account.OrganizationId,
                        Source = MailDeliveryReportSource.PaymentMethodExpiry,
                        Outcome = MailDeliveryReportOutcome.Published,
                        SubjectId = account.ItemId,
                        SubjectReference = method.ItemId,
                        MailMessageId = null,
                        ConsumerName = SubscriptionConstants.MailQueue,
                        Payload = payload,
                        CorrelationId = correlationId
                    },
                    cancellationToken);
            }

            // After the send, so a throw above leaves the card unmarked and the next pass retries.
            await _storedMethods.MarkExpiryNoticeSentAsync(
                tenantId, method.ItemId, expiry.Key, cancellationToken);
            sent++;

            _logger.LogInformation(
                "Card expiry notice queued TenantId={TenantId} BillingAccountId={BillingAccountId} " +
                "PaymentMethodId={PaymentMethodId} Expiry={Expiry}",
                PaymentLogValue.Id(tenantId),
                PaymentLogValue.Id(account.ItemId),
                PaymentLogValue.Id(method.ItemId),
                PaymentLogValue.Label(expiry.Key));
        }

        return sent;
    }
}
