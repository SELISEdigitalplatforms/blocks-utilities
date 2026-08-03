using Blocks.Genesis;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

/// <param name="ActorId">
/// Who the caller is, for deriving the shopper reference. Falls back to the email when no user
/// id is present, so it is not necessarily a user id and must never be treated as one — the
/// shopper reference is an HMAC over this value, and changing how it is derived would orphan
/// every saved card.
/// </param>
/// <param name="UserId">
/// The authenticated user id, recorded on the payment so it can be joined back to a user.
/// Null for callers that authenticate without one, such as machine-to-machine clients.
/// </param>
public sealed record PaymentExecutionContext(
    string TenantId,
    string ActorId,
    string? OrganizationId,
    string? UserId = null);
