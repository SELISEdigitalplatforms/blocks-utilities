using Payment.DomainService.Enums;

namespace Payment.DomainService.Services;

public sealed class ProviderCredentialRotationPlan
{
    public bool IsSuccess { get; init; }

    public string CredentialJson { get; init; } = string.Empty;

    public string TenantSecurityJson { get; init; } = string.Empty;

    public PaymentFailureKind FailureKind { get; init; }

    public string ErrorCode { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public static ProviderCredentialRotationPlan Success(
        string credentialJson,
        string tenantSecurityJson) =>
        new()
        {
            IsSuccess = true,
            CredentialJson = credentialJson,
            TenantSecurityJson = tenantSecurityJson
        };

    public static ProviderCredentialRotationPlan Failure(
        PaymentFailureKind failureKind,
        string errorCode,
        string errorMessage) =>
        new()
        {
            FailureKind = failureKind,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
}
