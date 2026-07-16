using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Payment.DomainService.Services;

public sealed record CheckoutCallbackState(
    string TenantId,
    string PaymentDetailId,
    string ProviderName,
    DateTime IssuedAtUtc,
    DateTime ExpiresAtUtc,
    string Nonce);
