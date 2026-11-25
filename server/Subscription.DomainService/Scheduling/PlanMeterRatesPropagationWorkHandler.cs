using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;

namespace Subscription.DomainService.Scheduling;

/// <summary>Carries out <see cref="SubscriptionWorkType.PlanMeterRatesPropagation"/>.</summary>
public sealed class PlanMeterRatesPropagationWorkHandler : ISubscriptionWorkHandler
{
    private const string KeyPrefix = "plan-meter-rates";

    private readonly ISubscriptionCatalogueRepository _catalogue;
    private readonly ExistingSubscriberPriceTerms _existingSubscribers;

    public PlanMeterRatesPropagationWorkHandler(
        ISubscriptionCatalogueRepository catalogue,
        ExistingSubscriberPriceTerms existingSubscribers)
    {
        _catalogue = catalogue;
        _existingSubscribers = existingSubscribers;
    }

    public SubscriptionWorkType WorkType => SubscriptionWorkType.PlanMeterRatesPropagation;

    /// <summary><c>plan-meter-rates/{planId}/{correlationId}/{meterKey}</c>; see <see cref="PlanMeterWorkKey"/>.</summary>
    public static string KeyFor(string planId, string correlationId, string meterKey) =>
        PlanMeterWorkKey.For(KeyPrefix, planId, correlationId, meterKey);

    /// <summary>The meter key a <see cref="KeyFor"/> key ends with, or null when the key is not one.</summary>
    public static string? MeterKeyOf(string workKey) => PlanMeterWorkKey.MeterKeyOf(KeyPrefix, workKey);

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (MeterKeyOf(work.WorkKey) is not { } meterKey)
        {
            return SubscriptionWorkOutcome.Permanent(
                "plan_meter_rates_key_invalid", "The work key does not name a meter.");
        }

        var plan = await _catalogue.GetPlanAsync(work.TenantId, work.AggregateId, cancellationToken);
        var meter = plan?.Meters.Find(candidate =>
            string.Equals(candidate.MeterKey, meterKey, StringComparison.Ordinal));

        if (plan is null || meter is null)
        {
            return SubscriptionWorkOutcome.Permanent(
                "plan_meter_not_found", "The plan or its meter no longer exists.");
        }

        // Who saved the rates is on the catalogue's own PlanMeterRatesUpdate audit event, joined
        // to each subscriber's by the correlation id this work carries.
        var extension = await _existingSubscribers.ApplyMeterRatesAsync(
            work.TenantId,
            plan.ItemId,
            meterKey,
            meter.RateTables,
            requestedByName: null,
            actorId: null,
            userId: null,
            work.CorrelationId,
            cancellationToken);

        // Safe to repeat: anyone already on these rates is skipped and not emailed again.
        return extension.Failed > 0
            ? SubscriptionWorkOutcome.Retry(
                "plan_meter_rates_partial",
                $"{extension.Failed} subscriber(s) could not be repriced on this attempt.")
            : SubscriptionWorkOutcome.Completed();
    }
}
