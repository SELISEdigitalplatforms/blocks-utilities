using Payment.DomainService.Enums;
using Payment.DomainService.Services;

namespace Subscription.DomainService.Services;

public sealed class SubscriptionContextResolver : ISubscriptionContextResolver
{
    private readonly IPaymentExecutionContextResolver _paymentContextResolver;
    private readonly IPaymentOrganizationResolver _organizationResolver;

    public SubscriptionContextResolver(
        IPaymentExecutionContextResolver paymentContextResolver,
        IPaymentOrganizationResolver organizationResolver)
    {
        _paymentContextResolver = paymentContextResolver;
        _organizationResolver = organizationResolver;
    }

    public async Task<SubscriptionContextResolution> ResolveAsync(
        string correlationId,
        string? requestedOrganizationId,
        CancellationToken cancellationToken)
    {
        var resolution = _paymentContextResolver.Resolve(correlationId);

        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return SubscriptionContextResolution.Unresolved(
                PaymentFailureKind.Unavailable,
                "subscription_context_missing",
                "Authenticated tenant context is unavailable.");
        }

        var context = resolution.Context;

        // The same rule payment writes and reads already apply: trusted only for the platform
        // console (Payment:ConsoleOrganizationId), ignored for everyone else. One policy, shared
        // rather than duplicated — see IPaymentOrganizationResolver's own remarks.
        var organization = await _organizationResolver.ResolveAsync(
            requestedOrganizationId,
            context,
            correlationId,
            cancellationToken);

        if (organization.Failure is { } failure)
        {
            return SubscriptionContextResolution.Unresolved(
                failure.FailureKind,
                failure.ErrorCode,
                failure.ErrorMessage);
        }

        if (string.IsNullOrWhiteSpace(organization.OrganizationId))
        {
            // Fails closed rather than falling back to tenant-wide scope. A subscription
            // belongs to an organization, so a caller without one has nothing to be told about
            // — and answering anyway would mean answering for somebody else. Stricter than the
            // payment resolver it wraps, which is content to leave this blank.
            return SubscriptionContextResolution.Unresolved(
                PaymentFailureKind.Unavailable,
                "subscription_organization_missing",
                "An organization is required to resolve a subscription.");
        }

        return SubscriptionContextResolution.Resolved(
            new SubscriptionContext(
                context.TenantId,
                organization.OrganizationId,
                context.ActorId,
                context.UserId));
    }
}
