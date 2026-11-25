using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;

namespace Subscription.DomainService.Scheduling;

/// <summary>Carries out <see cref="SubscriptionWorkType.PlanCreditTermsPropagation"/>.</summary>
public sealed class PlanCreditTermsPropagationWorkHandler : ISubscriptionWorkHandler
{
    private const string KeyPrefix = "plan-credit-terms";

    private readonly ISubscriptionCatalogueRepository _catalogue;
    private readonly ExistingSubscriberPriceTerms _existingSubscribers;

    public PlanCreditTermsPropagationWorkHandler(
        ISubscriptionCatalogueRepository catalogue,
        ExistingSubscriberPriceTerms existingSubscribers)
    {
        _catalogue = catalogue;
        _existingSubscribers = existingSubscribers;
    }

    public SubscriptionWorkType WorkType => SubscriptionWorkType.PlanCreditTermsPropagation;

    /// <summary><c>plan-credit-terms/{planId}/{correlationId}/{meterKey}</c>; see <see cref="PlanMeterWorkKey"/>.</summary>
    public static string KeyFor(string planId, string correlationId, string meterKey) =>
        PlanMeterWorkKey.For(KeyPrefix, planId, correlationId, meterKey);

    public async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (PlanMeterWorkKey.MeterKeyOf(KeyPrefix, work.WorkKey) is not { } meterKey)
        {
            return SubscriptionWorkOutcome.Permanent(
                "plan_credit_terms_key_invalid", "The work key does not name a meter.");
        }

        var plan = await _catalogue.GetPlanAsync(work.TenantId, work.AggregateId, cancellationToken);
        var meter = plan?.Meters.Find(candidate =>
            string.Equals(candidate.MeterKey, meterKey, StringComparison.Ordinal));

        if (plan is null || meter is null)
        {
            return SubscriptionWorkOutcome.Permanent(
                "plan_meter_not_found", "The plan or its meter no longer exists.");
        }

        // The catalogue's terms as they stand now — null when credits were taken off the plan — so
        // two saves in a row push the latest. Who saved them is on the plan's
        // PlanCreditTermsUpdate audit event, joined by this work's correlation id.
        var extension = await _existingSubscribers.ApplyCreditTermsAsync(
            work.TenantId,
            plan.ItemId,
            meterKey,
            meter.TopUp,
            requestedByName: null,
            actorId: null,
            userId: null,
            work.CorrelationId,
            cancellationToken);

        return extension.Failed > 0
            ? SubscriptionWorkOutcome.Retry(
                "plan_credit_terms_partial",
                $"{extension.Failed} subscriber(s) could not be updated on this attempt.")
            : SubscriptionWorkOutcome.Completed();
    }
}
