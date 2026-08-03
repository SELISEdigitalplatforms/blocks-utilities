namespace Payment.DomainService.Services;

public sealed class ProviderFailureReasonMapper :
    IProviderFailureReasonMapper
{
    public ProviderFailureReason? Map(
        string? eventCode,
        bool success,
        string? providerReason)
    {
        if (success)
        {
            return null;
        }

        var reason = providerReason?.Trim() ?? string.Empty;

        if (reason.Contains(
                "hasn't been captured",
                StringComparison.OrdinalIgnoreCase) ||
            reason.Contains(
                "not captured",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderFailureReason(
                "payment_not_captured",
                "The payment has not been captured.");
        }

        if (reason.Contains(
                "insufficient",
                StringComparison.OrdinalIgnoreCase) ||
            reason.Contains(
                "exceeds",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderFailureReason(
                "insufficient_provider_balance",
                "The requested amount is not available.");
        }

        if (reason.Contains(
                "expired",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderFailureReason(
                "payment_authorization_expired",
                "The payment authorization has expired.");
        }

        var operation = eventCode?.ToUpperInvariant() switch
        {
            "CAPTURE" or "CAPTURE_FAILED" => "capture",
            "REFUND" or "REFUND_FAILED" or
                "CANCEL_OR_REFUND" => "fund return",
            _ => "payment operation"
        };

        return new ProviderFailureReason(
            $"provider_{operation.Replace(' ', '_')}_rejected",
            $"The provider rejected the {operation}.");
    }
}
