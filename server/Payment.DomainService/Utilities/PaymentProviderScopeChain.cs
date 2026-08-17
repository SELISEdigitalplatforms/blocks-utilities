namespace Payment.DomainService.Utilities;

/// <summary>
/// The organizations whose provider configuration may serve a caller, most specific first.
/// </summary>
/// <remarks>
/// Pure and synchronous for the same reason as <see cref="PaymentOrganizationScope"/>: one
/// definition, so a payment cannot be taken through a configuration that a later capture, refund
/// or renewal then fails to find.
/// <para>
/// The two answer different questions and should not be confused.
/// <see cref="PaymentOrganizationScope"/> asks whether a caller may <em>name</em> an organization,
/// which is authorization. This asks which configurations may <em>serve</em> the organization
/// already resolved, which is lookup — it grants nobody any reach they did not have, because the
/// tenant is fixed by the caller's token before any of these are tried.
/// </para>
/// </remarks>
public static class PaymentProviderScopeChain
{
    /// <summary>
    /// Candidates in resolution order, without repeats. A null entry means the tenant-level
    /// configuration.
    /// </summary>
    /// <remarks>
    /// The order is the whole rule:
    /// <list type="number">
    /// <item>the caller's own organization — a configuration of its own always wins, so nothing
    /// a tenant has already set up changes meaning;</item>
    /// <item>null — the tenant-level configuration that predates organization scoping;</item>
    /// <item>the console's, when <see cref="PaymentOptions.TreatConsoleOrganizationAsTenantWide"/>
    /// allows it, because a tenant that configured one merchant account from the console meant it
    /// for the tenant.</item>
    /// </list>
    /// De-duplicated so the console's own callers do not query the same organization twice.
    /// </remarks>
    public static IReadOnlyList<string?> Candidates(
        string? callerOrganizationId,
        PaymentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var caller = callerOrganizationId?.Trim();
        var candidates = new List<string?>(3);

        if (!string.IsNullOrEmpty(caller))
        {
            candidates.Add(caller);
        }

        candidates.Add(null);

        var console = options.ConsoleOrganizationId?.Trim();

        if (options.TreatConsoleOrganizationAsTenantWide &&
            !string.IsNullOrEmpty(console) &&
            !string.Equals(console, caller, StringComparison.Ordinal))
        {
            candidates.Add(console);
        }

        return candidates;
    }
}
