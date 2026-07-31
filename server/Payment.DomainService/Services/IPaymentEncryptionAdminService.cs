using Payment.DomainService.Enums;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

/// <summary>
/// Operator-facing view of the calling scope's encryption key ring.
/// </summary>
/// <remarks>
/// Replaces the startup check that a single ring made possible. With a ring per organization
/// there is nothing to verify at boot — the service does not yet know which organizations exist
/// — so a broken ring would otherwise surface only when someone tries to pay. This answers
/// "is this organization's ring healthy" without attempting a payment.
/// <para>
/// The scope always comes from the caller's own context, never from a parameter, so this cannot
/// be used to inspect or rewrite another tenant's data.
/// </para>
/// </remarks>
public interface IPaymentEncryptionAdminService
{
    Task<PaymentEncryptionHealthResult> GetHealthAsync(
        string correlationId,
        CancellationToken cancellationToken);

    Task<PaymentEncryptionReEncryptionResult> ReEncryptAsync(
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed record PaymentEncryptionHealthResult(
    bool IsSuccess,
    PaymentEncryptionHealthResponse? Health,
    PaymentFailureKind FailureKind,
    string ErrorCode,
    string ErrorMessage,
    string CorrelationId);

public sealed record PaymentEncryptionReEncryptionResult(
    bool IsSuccess,
    PaymentEncryptionReEncryptionResponse? Summary,
    PaymentFailureKind FailureKind,
    string ErrorCode,
    string ErrorMessage,
    string CorrelationId);
