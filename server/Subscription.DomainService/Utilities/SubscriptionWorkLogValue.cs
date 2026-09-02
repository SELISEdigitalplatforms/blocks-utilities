using Payment.DomainService.Utilities;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Log rendering for the identifiers background work carries.
/// </summary>
/// <remarks>
/// <see cref="PaymentLogValue.Id"/> renders an absent identifier as <c>missing</c>, which reads as
/// something that should have been there and was lost. Most background work is about one
/// subscription and that reading is right. A tenant-wide sweep is about none of them, and printing
/// the same word for both left an operator unable to tell a scope from a defect — the difference
/// this exists to say out loud.
/// </remarks>
public static class SubscriptionWorkLogValue
{
    /// <summary>Absent because the work is not about any one aggregate.</summary>
    private const string NotApplicable = "none";

    /// <summary>
    /// The aggregate a work item acts on, or <c>none</c> when it acts on the tenant as a whole.
    /// </summary>
    public static string AggregateId(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? NotApplicable
            : PaymentLogValue.Id(value);
}
