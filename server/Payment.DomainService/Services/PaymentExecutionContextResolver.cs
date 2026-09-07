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
        // run out with nothing.
        //
        // The platform now carries that application's identifier itself, which is where it is
        // read from — the same value the token holds, without parsing anything, and present on
        // any path that resolves a context rather than only on one carrying a bearer token.
        // Reading it back out of the token stays as the rung below: it costs nothing when the
        // context supplies the identifier, and it is what an older Blocks release falls back to.
        var actorId = userId
            ?? Present(blocksContext?.Email)
            ?? Application(blocksContext?.ClientId)
            ?? Present(PaymentTokenActor.ClientId(blocksContext?.OAuthToken))
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
    /// An application named the same way whichever rung supplied it.
    /// </summary>
    /// <remarks>
    /// Wearing <see cref="PaymentTokenActor.ClientPrefix"/> for the same reason the token rung
    /// does: a bare identifier in an actor field cannot be told apart from a person's later, and
    /// the same application recorded under two spellings — once from the context, once from the
    /// token — would read as two actors in an audit trail.
    /// </remarks>
    private static string? Application(string? clientId) =>
        Present(clientId) is { } identifier
            ? PaymentTokenActor.ClientPrefix + identifier
            : null;
}
