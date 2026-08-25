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

        // The context reports an absent user id as an empty string rather than null, so a
        // null-coalescing fallback to the email never fired and such callers were rejected
        // outright. Treating blank as absent is what makes the fallback work as intended.
        var userId = Present(blocksContext?.UserId);
        var actorId = userId ?? Present(blocksContext?.Email) ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(actorId))
        {
            return new PaymentContextResolution(
                new PaymentExecutionContext(
                    tenantId,
                    actorId,
                    blocksContext?.OrganizationId,
                    userId,
                    // The display name first, because that is what a person is called; the login
                    // name only as the fallback. Both may be absent for a machine-to-machine caller,
                    // which is then named by what it did rather than by an empty string.
                    Present(blocksContext?.DisplayName) ?? Present(blocksContext?.UserName),
                    Present(blocksContext?.Email)),
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

    private static string? Present(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
