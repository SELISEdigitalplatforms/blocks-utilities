using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Simulation;

public sealed class MarkPaymentSucceededRequest
{
    /// <summary>Required — the console has no subscription of its own; see the state endpoint.</summary>
    public string? OrganizationId { get; set; }

    public SubscriptionPaymentPurpose PaymentPurpose { get; set; }

    /// <summary>
    /// Stands in for the provider's own reference. Generated when omitted; supplying it lets a
    /// caller keep the same reference across a retried call and see the real system's dedup key
    /// resolve identically.
    /// </summary>
    public string? ProviderReference { get; set; }

    /// <summary>
    /// Whether to run the same processor a real settlement would (the activation sweep for
    /// <see cref="SubscriptionPaymentPurpose.InitialCharge"/>, the renewal service for
    /// <see cref="SubscriptionPaymentPurpose.Renewal"/>). Defaults to true; false only records
    /// the settlement fact and reports state, useful for inspecting an intermediate step.
    /// </summary>
    public bool RunProcessor { get; set; } = true;
}

public sealed class MarkPaymentFailedRequest
{
    public string? OrganizationId { get; set; }

    public SubscriptionPaymentPurpose PaymentPurpose { get; set; }

    public SimulatedPaymentFailureKind FailureKind { get; set; }

    /// <summary>Overrides the failure kind's default error code, for a scenario naming its own.</summary>
    public string? ErrorCode { get; set; }

    public bool RunProcessor { get; set; } = true;
}
