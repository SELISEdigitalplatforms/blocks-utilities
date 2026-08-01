using Payment.DomainService.Enums;

namespace Payment.DomainService.Responses;

public sealed class PaymentProviderListResult
{
    public bool IsSuccess { get; init; }

    public IReadOnlyList<PaymentProviderResponse> Providers { get; init; } =
        [];

    public PaymentFailureKind FailureKind { get; init; }

    public string ErrorCode { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public string CorrelationId { get; init; } = string.Empty;

    public static PaymentProviderListResult Success(
        IReadOnlyList<PaymentProviderResponse> providers,
        string correlationId) =>
        new()
        {
            IsSuccess = true,
            Providers = providers,
            CorrelationId = correlationId
        };

    public static PaymentProviderListResult Failure(
        PaymentFailureKind failureKind,
        string errorCode,
        string errorMessage,
        string correlationId) =>
        new()
        {
            FailureKind = failureKind,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            CorrelationId = correlationId
        };
}
