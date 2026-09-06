namespace Payment.DomainService.Enums;

public enum PaymentFailureKind
{
    None,
    Validation,
    NotFound,
    Conflict,
    RateLimited,
    ProviderRejected,
    ProviderFailure,

    /// <summary>
    /// The caller could not be identified well enough to act on: no tenant, no actor, or no
    /// organization to scope the answer to.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Unavailable"/> because the two ask opposite things of a client.
    /// A token that carries no identity will carry none on the next attempt either, and
    /// reporting it as a transient failure told integrations — and the load balancers in front
    /// of them — to retry something that can never succeed. This says the request needs a
    /// different token, not a later one.
    /// </remarks>
    Unauthenticated,

    Unavailable,
    Timeout,
    Unexpected
}
