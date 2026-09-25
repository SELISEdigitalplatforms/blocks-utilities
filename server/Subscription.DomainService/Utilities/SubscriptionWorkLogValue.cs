using Microsoft.Extensions.Logging;
using Subscription.DomainService.Enums;
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

    /// <summary>
    /// Whether a work type's aggregate id is a subscription id.
    /// </summary>
    /// <remarks>
    /// Not for the financial document work: issue is keyed on a payment for a charge and delivery on
    /// a document. Labelling those ids <c>SubscriptionId</c> is worse than leaving it out, because a
    /// scope value overrides a template value of the same name, so every line of that work, even
    /// one that names the real subscription, rendered the payment or document id in its place.
    /// </remarks>
    public static bool AggregateIsSubscription(SubscriptionWorkType workType) =>
        workType is not (SubscriptionWorkType.FinancialDocumentIssue
            or SubscriptionWorkType.FinancialDocumentDelivery);

    /// <summary>
    /// Scopes the lines that follow to one subscription, for code that learns the subscription only
    /// part-way through: <see cref="SearchableIdLogSink"/> then puts it into each line's message.
    /// </summary>
    public static IDisposable? SubscriptionScope(ILogger logger, string? subscriptionId) =>
        string.IsNullOrWhiteSpace(subscriptionId)
            ? null
            : logger.BeginScope(new Dictionary<string, object?>
            {
                [SearchableIdLogSink.SubscriptionId] = PaymentLogValue.Id(subscriptionId)
            });
}
