using Blocks.Genesis;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

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

        // A client-credentials caller is named by neither of the first two: it authenticates as
        // an application, so the context reports no user id and no email and the ladder used to
        // run out with nothing. The platform carries that application's own identifier, which is
        // what names it instead of the request being refused for not coming from a person.
        var actorId = userId
            ?? Present(blocksContext?.Email)
            ?? Application(blocksContext?.ClientId)
            ?? string.Empty;

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
                PaymentFailureKind.Unauthenticated,
                "payment_context_missing",
                "Authenticated tenant context is unavailable.",
                correlationId));
    }

    private static string? Present(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Marks the actor as an application rather than a person.
    /// </summary>
    /// <remarks>
    /// A bare client id is a GUID, and a GUID in a field named for an actor cannot be told apart
    /// from a user id by whoever reads the audit trail a year later. The same reasoning keeps the
    /// email fallback out of <c>UserId</c>: a fallback names the caller without being allowed to
    /// pass itself off as the thing it stood in for.
    /// </remarks>
    private static string? Application(string? clientId) =>
        Present(clientId) is { } identifier ? "client:" + identifier : null;
}
