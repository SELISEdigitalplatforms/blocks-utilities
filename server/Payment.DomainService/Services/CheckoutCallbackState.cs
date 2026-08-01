using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Payment.DomainService.Services;

/// <param name="OrganizationId">
/// The organization whose configuration started this payment.
/// </param>
/// <remarks>
/// Carried in the state rather than read from the payment because the shopper's return has to
/// resolve a configuration in order to verify this very state — the payment cannot be trusted,
/// or even loaded, until that verification succeeds. Appended last and optional so a state
/// issued before organizations were scoped still reads, falling back to the tenant's own
/// configuration.
/// </remarks>
public sealed record CheckoutCallbackState(
    string TenantId,
    string PaymentDetailId,
    string ProviderName,
    DateTime IssuedAtUtc,
    DateTime ExpiresAtUtc,
    string Nonce,
    string? OrganizationId = null);
