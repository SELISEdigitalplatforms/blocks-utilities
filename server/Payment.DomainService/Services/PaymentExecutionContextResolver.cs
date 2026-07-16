using Blocks.Genesis;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed class PaymentExecutionContextResolver : IPaymentExecutionContextResolver
{
    public PaymentContextResolution Resolve(string correlationId)
    {
        var blocksContext = BlocksContext.GetContext();
        var tenantId = blocksContext?.TenantId ?? string.Empty;
        var actorId = blocksContext?.UserId ?? blocksContext?.Email ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(actorId))
        {
            return new PaymentContextResolution(
                new PaymentExecutionContext(tenantId, actorId, blocksContext?.OrganizationId),
                null);
        }

        return new PaymentContextResolution(
            null,
            PaymentOperationResult.Failure(
                PaymentFailureKind.Unavailable,
                "payment_context_missing",
                "Authenticated tenant context is unavailable.",
                correlationId));
    }
}
