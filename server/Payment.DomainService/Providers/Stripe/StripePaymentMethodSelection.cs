using Payment.DomainService.Entities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Decides which payment methods a Checkout Session offers, from the provider's configuration.
/// </summary>
/// <remarks>
/// Stripe offers three ways to answer this, and the first two are mutually exclusive on the
/// wire: naming <c>payment_method_types</c> explicitly, naming a
/// <c>payment_method_configuration</c> assembled in the Dashboard, or naming neither and letting
/// the account's default configuration decide. Sending the first two together is rejected, so
/// this type picks one.
/// <para>
/// Naming neither has always been the behaviour here — no <c>payment_method_types</c> has ever
/// been sent from this service — which is why a Dashboard change already reaches an ordinary
/// payment. What it does not reach is a checkout that stores the card, and that is the subtlety
/// this type exists to hold; see <see cref="ReusableOffSession"/>.
/// </para>
/// </remarks>
public static class StripePaymentMethodSelection
{
    /// <summary>
    /// The methods Stripe can charge again later with nobody present.
    /// </summary>
    /// <remarks>
    /// A renewal is an off-session charge against a stored mandate, and Stripe only establishes
    /// such a mandate for methods that support one. A checkout carrying
    /// <c>setup_future_usage=off_session</c> is therefore already narrowed by Stripe to this
    /// set — which is why a Dashboard that enables TWINT and Klarna still shows neither on a
    /// subscription's first charge, and why that is correct rather than a defect: a subscription
    /// bought with TWINT could never renew itself.
    /// <para>
    /// Kept here so an explicitly configured list is narrowed the same way, rather than being
    /// sent whole for Stripe to reject the whole session over. PayPal is present because Stripe
    /// does support recurring PayPal — once the account is approved for it, which is an account
    /// question rather than a code one.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> ReusableOffSession = new(StringComparer.Ordinal)
    {
        "card",
        "link",
        "paypal",
        "sepa_debit",
        "us_bank_account",
        "bacs_debit",
        "au_becs_debit"
    };

    /// <summary>
    /// Applies the provider's configuration to a Checkout Session form.
    /// </summary>
    /// <param name="form">The session form being built.</param>
    /// <param name="provider">The configuration to read the selection from.</param>
    /// <param name="requiresOffSessionReuse">
    /// Whether the session also asks Stripe to store the method for later off-session charges.
    /// When it does, an explicit list is narrowed to what can actually be charged again.
    /// </param>
    /// <returns>
    /// The methods dropped because they cannot be reused off-session, so a caller can say so in
    /// a log. Empty when nothing was dropped, which is the ordinary case.
    /// </returns>
    public static IReadOnlyCollection<string> Apply(
        StripeForm form,
        PaymentProvider provider,
        bool requiresOffSessionReuse)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(provider);

        var configured = Normalize(provider.CheckoutPaymentMethodTypes);

        if (configured.Count > 0)
        {
            var offered = requiresOffSessionReuse
                ? configured.Where(ReusableOffSession.Contains).ToList()
                : configured;

            // Every configured method was unusable here. Sending an empty array is not the same
            // as sending nothing — Stripe rejects it — and falling back to the configuration id
            // would answer a question the operator already answered differently. Letting the
            // account default decide is the one remaining option that still produces a working
            // checkout rather than no checkout at all.
            if (offered.Count == 0)
            {
                return configured;
            }

            for (var index = 0; index < offered.Count; index++)
            {
                form.Add($"payment_method_types[{index}]", offered[index]);
            }

            return configured.Except(offered, StringComparer.Ordinal).ToList();
        }

        // Only when no explicit list was given: Stripe rejects a session carrying both.
        if (!string.IsNullOrWhiteSpace(provider.PaymentMethodConfigurationId))
        {
            form.Add(
                "payment_method_configuration",
                provider.PaymentMethodConfigurationId.Trim());
        }

        return [];
    }

    /// <summary>
    /// Trims, lower-cases and de-duplicates a configured list, preserving the order it was
    /// authored in — Stripe renders the methods in the order they arrive, so that order is a
    /// presentation decision the operator made.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string>? values)
    {
        if (values == null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>();

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim().ToLowerInvariant();

            if (seen.Add(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        return normalized;
    }

    /// <summary>Whether a method can back a charge raised with nobody present.</summary>
    public static bool CanBeReusedOffSession(string method) =>
        !string.IsNullOrWhiteSpace(method) &&
        ReusableOffSession.Contains(method.Trim().ToLowerInvariant());
}
