namespace Payment.DomainService.Requests;

/// <summary>
/// Asks a provider to collect and store a card without charging it.
/// </summary>
/// <remarks>
/// Never bound from an HTTP body. Collecting a card costs nothing and therefore has none of the
/// natural limits a charge has — no amount to check, no money to reconcile — so the ability to
/// mint one belongs to callers inside the process that already know why they want it. The
/// subscription module is the only one today.
/// </remarks>
public sealed class CreatePaymentMethodSetupRequest
{
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// The currency the card will eventually be charged in. Carried because Stripe wants it on a
    /// setup session, and because a card collected for one currency says nothing about another —
    /// not because anything here is priced.
    /// </summary>
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>The order this setup belongs to, so it can be found again the way a charge can.</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Shown to the shopper on the provider's page.</summary>
    public string? Description { get; set; }

    /// <summary>The subscriber, as opposed to the merchant taking the money later.</summary>
    public string? CustomerOrganizationId { get; set; }

    public string? CustomerEmail { get; set; }

    /// <summary>
    /// Which organization's provider configuration to use. Omit for the caller's own.
    /// </summary>
    public string? OrganizationId { get; set; }
}
