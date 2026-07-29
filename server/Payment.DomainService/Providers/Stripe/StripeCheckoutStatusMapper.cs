using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Maps a Checkout Session's outcome onto the shared vocabulary.
/// </summary>
/// <remarks>
/// Stripe reports two fields, and both matter. A session can be <c>complete</c> while its
/// payment_status is still <c>unpaid</c>, which happens with delayed payment methods such as
/// bank debits. Treating that as paid is the classic mistake, so it maps to pending and waits
/// for the asynchronous webhook.
/// </remarks>
public sealed class StripeCheckoutStatusMapper : ICheckoutStatusMapper
{
    private const char Separator = '/';

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.StripeProvider,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Joins the two Stripe fields so both survive to <see cref="Normalize"/>.</summary>
    public static string Compose(string? status, string? paymentStatus) =>
        $"{status}{Separator}{paymentStatus}";

    public string Normalize(string providerStatus)
    {
        ArgumentNullException.ThrowIfNull(providerStatus);

        var parts = providerStatus.Split(Separator, 2);
        var status = parts[0].Trim().ToLowerInvariant();
        var paymentStatus = parts.Length > 1
            ? parts[1].Trim().ToLowerInvariant()
            : string.Empty;

        return status switch
        {
            "expired" => "expired",
            "open" => "paymentPending",
            "complete" => paymentStatus switch
            {
                "paid" or "no_payment_required" => "completed",
                _ => "paymentPending"
            },
            _ => "unknown"
        };
    }

    public string ToRedirectStatus(string normalizedStatus) => normalizedStatus switch
    {
        "completed" => PaymentRedirectStatuses.Success,
        "canceled" => PaymentRedirectStatuses.Cancelled,
        "refused" or "expired" => PaymentRedirectStatuses.Fail,
        _ => PaymentRedirectStatuses.Pending
    };
}
