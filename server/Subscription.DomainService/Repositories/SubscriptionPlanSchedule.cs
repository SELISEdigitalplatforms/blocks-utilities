using Subscription.DomainService.Entities;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Repositories;

/// <param name="FeePeriodFraction">
/// How much of a month <see cref="CurrentPeriodStartUtc"/> to <see cref="CurrentPeriodEndUtc"/>
/// actually is, when the target price bills on calendar boundaries and this period is a stub.
/// Default — a whole period — for every anniversary schedule.
/// </param>
public sealed record SubscriptionPlanSchedule(
    BillingSchedule FeeSchedule,
    DateTime CurrentPeriodStartUtc,
    DateTime CurrentPeriodEndUtc,
    DateTime NextFeeBillingAtUtc,
    BillingSchedule UsageSchedule,
    DateTime CurrentUsagePeriodStartUtc,
    DateTime CurrentUsagePeriodEndUtc,
    DateTime NextUsageBillingAtUtc,
    BillingDayFraction FeePeriodFraction = default);
