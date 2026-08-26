namespace Subscription.DomainService.Responses;

public sealed class SubscriptionBillingProfileResponse
{
    public string OrganizationId { get; init; } = string.Empty;

    public string LegalName { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string BillingContactName { get; init; } = string.Empty;

    public string BillingContactEmail { get; init; } = string.Empty;

    public BillingAddressResponse? Address { get; init; }

    public string? TaxRegistrationId { get; init; }

    /// <summary>
    /// Whether the profile carries everything a document must state. False blocks a paid
    /// subscription, so a client can prompt for the missing fields before the subscriber reaches a
    /// checkout that would refuse them.
    /// </summary>
    public bool IsComplete { get; init; }

    /// <summary>Which required fields are still empty. Empty when the profile is complete.</summary>
    public IReadOnlyList<string> MissingFields { get; init; } = [];

    public DateTime? LastUpdatedDateUtc { get; init; }
}

public sealed class BillingAddressResponse
{
    public string? Line1 { get; init; }

    public string? Line2 { get; init; }

    public string? City { get; init; }

    public string? Region { get; init; }

    public string? PostalCode { get; init; }

    public string? CountryCode { get; init; }
}
