using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

/// <summary>
/// What a billing profile is still missing before a document can be addressed to it.
/// </summary>
/// <remarks>
/// One place, because two answers to "is this profile usable" is how a subscriber gets past checkout
/// and then has an invoice issued with a blank name on it. The money paths ask this before they take
/// money; the profile endpoint asks it so a client can prompt for the same fields before the
/// subscriber ever reaches a checkout that would refuse them.
/// <para>
/// Field names are returned rather than a sentence, so a client can highlight the inputs rather than
/// showing a message it has to parse.
/// </para>
/// </remarks>
public static class BillingProfileCompleteness
{
    public static IReadOnlyList<string> MissingFields(SubscriptionBillingProfile? profile)
    {
        if (profile is null)
        {
            return
            [
                nameof(SubscriptionBillingProfile.LegalName),
                nameof(SubscriptionBillingProfile.BillingContactName),
                nameof(SubscriptionBillingProfile.BillingContactEmail)
            ];
        }

        var missing = new List<string>(3);

        if (string.IsNullOrWhiteSpace(profile.LegalName))
        {
            missing.Add(nameof(SubscriptionBillingProfile.LegalName));
        }

        if (string.IsNullOrWhiteSpace(profile.BillingContactName))
        {
            missing.Add(nameof(SubscriptionBillingProfile.BillingContactName));
        }

        if (string.IsNullOrWhiteSpace(profile.BillingContactEmail))
        {
            missing.Add(nameof(SubscriptionBillingProfile.BillingContactEmail));
        }

        return missing;
    }

    public static bool IsComplete(SubscriptionBillingProfile? profile) =>
        MissingFields(profile).Count == 0;
}
