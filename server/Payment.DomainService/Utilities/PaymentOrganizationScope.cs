namespace Payment.DomainService.Utilities;

/// <summary>
/// Whether a caller is allowed to name an organization other than the one their token carries.
/// </summary>
/// <remarks>
/// Pure and synchronous on purpose. Reads answer this question on every listing and cannot
/// afford a directory round trip, while writes answer it before deciding whether to make one;
/// splitting it into two implementations would let a payment be created under an organization
/// its own listing then refuses to show.
/// </remarks>
public static class PaymentOrganizationScope
{
    /// <summary>
    /// True only for the console. See <see cref="PaymentOptions.ConsoleOrganizationId"/> for why
    /// this exists and what it costs.
    /// </summary>
    /// <param name="contextOrganizationId">The organization from the caller's token.</param>
    /// <param name="options">Supplies the console's organization identifier.</param>
    public static bool RequestMayNameOrganization(
        string? contextOrganizationId,
        PaymentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var console = options.ConsoleOrganizationId?.Trim();

        // Configured empty, nobody may name one. A caller carrying no organization is not the
        // console either: it is an integration scoped to the whole tenant, and letting it
        // narrow to an organization it never proved membership of would be a widening dressed
        // as a filter.
        if (string.IsNullOrEmpty(console) ||
            string.IsNullOrWhiteSpace(contextOrganizationId))
        {
            return false;
        }

        return string.Equals(
            contextOrganizationId.Trim(),
            console,
            StringComparison.Ordinal);
    }
}
