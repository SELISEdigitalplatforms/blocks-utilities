namespace Payment.DomainService.Enums;

public enum PaymentFailureKind
{
    None, Validation, NotFound, Conflict, RateLimited, ProviderRejected, ProviderFailure, Unavailable, Timeout, Unexpected
}
