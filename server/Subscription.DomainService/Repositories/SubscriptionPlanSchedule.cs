using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed record SubscriptionPlanSchedule(
    BillingSchedule FeeSchedule,
    DateTime CurrentPeriodStartUtc,
    DateTime CurrentPeriodEndUtc,
    DateTime NextFeeBillingAtUtc,
    BillingSchedule UsageSchedule,
    DateTime CurrentUsagePeriodStartUtc,
    DateTime CurrentUsagePeriodEndUtc,
    DateTime NextUsageBillingAtUtc);
