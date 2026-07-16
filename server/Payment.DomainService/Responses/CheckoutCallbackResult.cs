using Payment.DomainService.Enums;

namespace Payment.DomainService.Responses;

public sealed record CheckoutCallbackResult(
    bool IsRedirect,
    string? RedirectUrl,
    PaymentFailureKind FailureKind,
    string ErrorCode,
    string ErrorMessage)
{
    public int? RetryAfterSeconds { get; init; }

    public static CheckoutCallbackResult Redirect(string url) => new(true, url, PaymentFailureKind.None, string.Empty, string.Empty);

    public static CheckoutCallbackResult Failure(
        PaymentFailureKind kind,
        string code,
        string message,
        int? retryAfterSeconds = null) =>
        new(false, null, kind, code, message)
        {
            RetryAfterSeconds = retryAfterSeconds
        };
}
