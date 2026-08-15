namespace Subscription.DomainService.Utilities;

/// <summary>
/// One occurrence of a cadence: when it opened, when it closes, and what to call it.
/// </summary>
/// <param name="Index">
/// How many whole cadences have elapsed since the anchor. Zero is the first period.
/// </param>
/// <param name="EndUtc">
/// Also the next boundary — the two are the same instant, so a separate "next due" calculation
/// cannot drift away from the period it belongs to.
/// </param>
public readonly record struct BillingPeriod(
    int Index,
    DateTime StartUtc,
    DateTime EndUtc,
    string Key);
