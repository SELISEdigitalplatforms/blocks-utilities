using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

/// <summary>
/// Decides which organization a write belongs to when the request names one.
/// </summary>
/// <remarks>
/// Shared by provider registration and payment creation on purpose. Two copies of this rule
/// would be two policies, and the difference would only show up as one endpoint trusting an
/// organization the other refuses. The tenant is never part of this decision — it comes from
/// the caller's token, always.
/// </remarks>
public interface IPaymentOrganizationResolver
{
    Task<PaymentOrganizationResolution> ResolveAsync(
        string? requestedOrganizationId,
        PaymentExecutionContext context,
        string correlationId,
        CancellationToken cancellationToken);
}

/// <param name="OrganizationId">The organization to stamp, when <paramref name="Failure"/> is null.</param>
/// <param name="Failure">Set when the request named an organization that cannot be trusted.</param>
/// <param name="RequestNamedTheOrganization">
/// Whether the caller was permitted to name the organization, which is true only of the
/// console. Recorded on the payment so a simulation is distinguishable afterwards from a
/// payment an application actually took.
/// </param>
public readonly record struct PaymentOrganizationResolution(
    string? OrganizationId,
    PaymentOperationResult? Failure,
    bool RequestNamedTheOrganization = false);
