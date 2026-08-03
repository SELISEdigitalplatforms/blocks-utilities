using Payment.DomainService.Enums;

namespace Payment.DomainService.Responses;

public sealed class PaymentProviderMutationResult
{
    public bool IsSuccess { get; init; }

    public PaymentProviderResponse? Provider { get; init; }

    public PaymentFailureKind FailureKind { get; init; }

    public string ErrorCode { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public Dictionary<string, string[]>? ValidationErrors { get; init; }

    public string CorrelationId { get; init; } = string.Empty;

    public static PaymentProviderMutationResult Success(
        PaymentProviderResponse provider,
        string correlationId) =>
        new()
        {
            IsSuccess = true,
            Provider = provider,
            CorrelationId = correlationId
        };

    public static PaymentProviderMutationResult Failure(
        PaymentFailureKind failureKind,
        string errorCode,
        string errorMessage,
        string correlationId,
        Dictionary<string, string[]>? validationErrors = null) =>
        new()
        {
            FailureKind = failureKind,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            CorrelationId = correlationId,
            ValidationErrors = validationErrors
        };
}
