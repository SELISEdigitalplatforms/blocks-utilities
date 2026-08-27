namespace Subscription.DomainService.Responses;

public sealed class SubscriptionMerchantProfileResponse
{
    public string LegalName { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public BillingAddressResponse? Address { get; init; }

    public string? TaxRegistrationId { get; init; }

    public string? SupportEmail { get; init; }

    public string? PaymentInstructions { get; init; }

    public string? LogoFileId { get; init; }

    /// <summary>Normalized six-digit hex with the leading <c>#</c>. Null renders the shared default.</summary>
    public string? PrimaryColor { get; init; }

    /// <summary>Normalized six-digit hex with the leading <c>#</c>. Null renders the shared default.</summary>
    public string? AccentColor { get; init; }

    /// <summary>
    /// Whether documents can be issued under this identity.
    /// </summary>
    /// <remarks>
    /// False blocks a user-initiated charge while enforcement is on, for the same reason an
    /// incomplete subscriber profile does: issuing an invoice that names no seller is not a
    /// presentation problem, it is a defective financial record.
    /// </remarks>
    public bool IsComplete { get; init; }

    public IReadOnlyList<string> MissingFields { get; init; } = [];

    /// <summary>
    /// Whether these values come from configuration rather than from this tenant's own profile.
    /// </summary>
    /// <remarks>
    /// True for an installation that predates per-tenant merchant identity. Worth surfacing because
    /// a configured identity is shared by every tenant in the deployment, so a console showing it as
    /// though it were this tenant's own would hide exactly the problem the reader needs to see.
    /// </remarks>
    public bool IsInheritedFromConfiguration { get; init; }

    public DateTime? LastUpdatedDateUtc { get; init; }
}
