using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Extends a discount or tax edit to the subscribers already on a price, from a later renewal,
/// telling each of them by email.
/// </summary>
/// <remarks>
/// An edit reaches existing subscribers only when the operator asks for it — see
/// <see cref="Requests.UpdatePriceDiscountRequest.ApplyToExistingSubscribers"/>. Each one is given
/// a <see cref="PendingPriceTerms"/> and its announcement in one compare-and-set, so a subscriber
/// is never repriced without having been told.
/// <para>
/// Left alone: a subscription already cancelling, which will not renew; and one with its own plan change
/// scheduled, which is leaving this price and was quoted the terms it is moving onto.
/// </para>
/// <para>
/// ponytail: runs inside the operator's request, one write per subscriber. Fine for the hundreds to
/// low thousands a price holds today; a price with tens of thousands should move this onto the work
/// queue. Repeating the same edit is safe and finishes whatever a failed run left: a subscriber
/// already holding these terms is written again with the same values.
/// </para>
/// </remarks>
public sealed class ExistingSubscriberPriceTerms
{
    private const int PageSize = 200;

    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ExistingSubscriberPriceTerms> _logger;

    public ExistingSubscriberPriceTerms(
        ISubscriptionRepository subscriptions,
        ISubscriptionOutboxEventFactory events,
        IOptionsMonitor<SubscriptionOptions> options,
        TimeProvider time,
        ILogger<ExistingSubscriberPriceTerms> logger)
    {
        _subscriptions = subscriptions;
        _events = events;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <param name="edit">
    /// What the operator changed, applied to the terms each subscriber is headed for — the ones a
    /// previous extension already scheduled, else the ones they are billed on now. Given the whole
    /// terms and returning them whole, so a discount edit leaves a scheduled tax edit standing and
    /// an edit nobody extended never reaches a subscriber through this.
    /// </param>
    public async Task<PriceTermsExtension> ExtendAsync(
        string tenantId,
        string priceId,
        Func<PriceTerms, PriceTerms> edit,
        string? requestedByName,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var now = _time.GetUtcNow().UtcDateTime;
        var noticeEndsAtUtc = now.AddDays(Math.Max(0, _options.CurrentValue.PriceTermsNoticeDays));
        var extension = new PriceTermsExtension();
        string? afterId = null;

        while (true)
        {
            var page = await _subscriptions.ListRenewableOnPriceAsync(
                tenantId, priceId, afterId, PageSize, cancellationToken);

            foreach (var subscription in page)
            {
                extension = extension.Add(await ExtendToAsync(
                    subscription, priceId, edit, noticeEndsAtUtc, now, requestedByName,
                    correlationId, cancellationToken));
            }

            if (page.Count < PageSize)
            {
                break;
            }

            afterId = page[^1].ItemId;
        }

        _logger.LogInformation(
            "Price terms extended to existing subscribers TenantId={TenantId} PriceHash={PriceHash} " +
            "Scheduled={Scheduled} Skipped={Skipped} Failed={Failed} CorrelationId={CorrelationId}",
            PaymentLogValue.Id(tenantId),
            PaymentLogValue.Hash(priceId),
            extension.Scheduled,
            extension.Skipped,
            extension.Failed,
            correlationId);

        return extension;
    }

    private async Task<Outcome> ExtendToAsync(
        SubscriptionDetail subscription,
        string priceId,
        Func<PriceTerms, PriceTerms> edit,
        DateTime noticeEndsAtUtc,
        DateTime now,
        string? requestedByName,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // One re-read on a lost race: a subscriber renewing or changing seats at the same instant is
        // ordinary, and failing them over it would leave a gap the operator cannot see.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (subscription.CancelAtPeriodEnd ||
                subscription.PendingPlanChange is not null ||
                subscription.Price.PriceId != priceId)
            {
                return Outcome.Skipped;
            }

            var held = subscription.PendingPriceTerms?.PriceId == priceId
                ? subscription.PendingPriceTerms
                : null;

            if (!TryFirstBoundaryFrom(subscription, noticeEndsAtUtc, out var effectiveAtUtc))
            {
                _logger.LogWarning(
                    "A price terms extension skipped a subscription whose schedule did not resolve " +
                    "SubscriptionId={SubscriptionId}",
                    PaymentLogValue.Id(subscription.ItemId));

                return Outcome.Failed;
            }

            var pending = new PendingPriceTerms
            {
                PriceId = priceId,
                Terms = edit(held?.Terms ?? PriceTerms.Of(subscription.Price)),
                // Never earlier than a date the subscriber was already given: a second edit inside
                // the notice window must not pull the first one's announced date forward.
                EffectiveAtUtc = held is not null && held.EffectiveAtUtc > effectiveAtUtc
                    ? held.EffectiveAtUtc
                    : effectiveAtUtc,
                RequestedAtUtc = now,
                RequestedByName = requestedByName
            };

            if (await _subscriptions.TrySetPendingPriceTermsAsync(
                    subscription.TenantId,
                    subscription.ItemId,
                    subscription.Version,
                    pending,
                    _events.CreatePriceTermsChangeScheduled(subscription, pending, correlationId),
                    cancellationToken))
            {
                return Outcome.Scheduled;
            }

            var reread = await _subscriptions.GetByIdAsync(
                subscription.TenantId, subscription.ItemId, cancellationToken);

            if (reread is null)
            {
                return Outcome.Skipped;
            }

            subscription = reread;
        }

        _logger.LogWarning(
            "A price terms extension lost the race for a subscription twice and left it unchanged " +
            "SubscriptionId={SubscriptionId}",
            PaymentLogValue.Id(subscription.ItemId));

        return Outcome.Failed;
    }

    /// <summary>
    /// The first renewal boundary at or after <paramref name="noticeEndsAtUtc"/>.
    /// </summary>
    /// <remarks>
    /// Walked from the end of what the subscriber has already paid for — never inside it — and past
    /// any opening year a calendar-aligned subscription has bought: that year's amount was frozen at
    /// checkout, so a boundary inside it would announce a change the bill does not show.
    /// </remarks>
    internal static bool TryFirstBoundaryFrom(
        SubscriptionDetail subscription,
        DateTime noticeEndsAtUtc,
        out DateTime boundaryUtc)
    {
        boundaryUtc = SubscriptionPaidPeriod.PaidThroughUtc(subscription);

        if (subscription.PendingAnnualPeriod is { } year && year.EndUtc > boundaryUtc)
        {
            boundaryUtc = year.EndUtc;
        }

        while (boundaryUtc < noticeEndsAtUtc)
        {
            if (!BillingPeriodCalculator.TryGetPeriod(subscription.FeeSchedule, boundaryUtc, out var period) ||
                period.EndUtc <= boundaryUtc)
            {
                return false;
            }

            boundaryUtc = period.EndUtc;
        }

        return true;
    }

    internal enum Outcome
    {
        Scheduled,
        Skipped,
        Failed
    }
}

/// <summary>How many subscribers an extension reached, passed over, and could not reach.</summary>
public readonly record struct PriceTermsExtension(int Scheduled, int Skipped, int Failed)
{
    internal PriceTermsExtension Add(ExistingSubscriberPriceTerms.Outcome outcome) => outcome switch
    {
        ExistingSubscriberPriceTerms.Outcome.Scheduled => this with { Scheduled = Scheduled + 1 },
        ExistingSubscriberPriceTerms.Outcome.Skipped => this with { Skipped = Skipped + 1 },
        _ => this with { Failed = Failed + 1 }
    };
}
