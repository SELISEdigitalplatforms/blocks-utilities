using Payment.DomainService.Enums;
using Payment.DomainService.Services;

namespace Subscription.DomainService.Services;

public sealed class SubscriptionContextResolver : ISubscriptionContextResolver
{
    private readonly IPaymentExecutionContextResolver _paymentContextResolver;

    public SubscriptionContextResolver(
        IPaymentExecutionContextResolver paymentContextResolver) =>
        _paymentContextResolver = paymentContextResolver;

    public SubscriptionContextResolution Resolve(string correlationId)
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

        if (string.IsNullOrWhiteSpace(context.OrganizationId))
        {
            // Fails closed rather than falling back to tenant-wide scope. A subscription
            // belongs to an organization, so a caller without one has nothing to be told about
            // — and answering anyway would mean answering for somebody else.
            return SubscriptionContextResolution.Unresolved(
                PaymentFailureKind.Unavailable,
                "subscription_organization_missing",
                "An organization is required to resolve a subscription.");
        }

        return SubscriptionContextResolution.Resolved(
            new SubscriptionContext(
                context.TenantId,
                context.OrganizationId,
                context.ActorId,
                context.UserId));
    }
}
