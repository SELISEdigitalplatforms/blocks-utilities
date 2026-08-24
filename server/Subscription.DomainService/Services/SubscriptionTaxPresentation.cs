using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// How a price's tax configuration is named to a caller.
/// </summary>
/// <remarks>
/// One place, because the rule has an edge that is easy to get wrong in each of the several
/// responses that carry it: a price authored before modes existed has a rate and no mode, and it is
/// calculated exclusively — so reporting nothing there would leave a client to guess at the very
/// thing the mode exists to state. An untaxed price reports nothing at all, since naming a mode for
/// a tax that does not apply invites a client to render "excluding CHF 0.00 tax".
/// </remarks>
public static class SubscriptionTaxPresentation
{
    public static string? Describe(int? taxRateBasisPoints, TaxMode? taxMode) =>
        taxRateBasisPoints > 0
            ? (taxMode ?? TaxMode.Exclusive).ToString()
            : null;

    public static string? Describe(PriceSnapshot price)
    {
        ArgumentNullException.ThrowIfNull(price);

        return Describe(price.TaxRateBasisPoints, price.TaxMode);
    }
}
