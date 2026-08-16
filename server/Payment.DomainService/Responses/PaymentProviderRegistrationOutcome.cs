using Payment.DomainService.Enums;

namespace Payment.DomainService.Responses;

/// <summary>
/// What happened for one organization in a registration request.
/// </summary>
/// <remarks>
/// Per organization rather than per request because the organizations are independent: each
/// gets its own key ring, its own encryption, and its own row. One failing says nothing about
/// the others, and a single verdict for the whole request would have to either discard the
/// successes or hide the failures.
/// </remarks>
public sealed class PaymentProviderRegistrationOutcome
{
    /// <summary>Null means the tenant-level configuration, which is a real scope and not an absent one.</summary>
    public string? OrganizationId { get; init; }

    public bool IsSuccess { get; init; }

    /// <summary><c>REGISTERED</c> or <c>FAILED</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>The new configuration's identifier, when it was created.</summary>
    public string? PaymentProviderId { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>Carried so a single-organization request can be mapped to the status code it always used.</summary>
    public PaymentFailureKind FailureKind { get; init; }

    public static PaymentProviderRegistrationOutcome Registered(
        string? organizationId,
        string paymentProviderId) => new()
    {
        OrganizationId = organizationId,
        IsSuccess = true,
        Status = "REGISTERED",
        PaymentProviderId = paymentProviderId
    };

    public static PaymentProviderRegistrationOutcome Failed(
        string? organizationId,
        PaymentOperationResult failure) => new()
    {
        OrganizationId = organizationId,
        IsSuccess = false,
        Status = "FAILED",
        ErrorCode = failure?.ErrorCode,
        ErrorMessage = failure?.ErrorMessage,
        FailureKind = failure?.FailureKind ?? PaymentFailureKind.Unexpected
    };
}

/// <summary>
/// The body returned when a registration named more than one organization.
/// </summary>
public sealed class PaymentProviderRegistrationResponse
{
    public string ProviderName { get; init; } = string.Empty;

    public IReadOnlyList<PaymentProviderRegistrationOutcome> Organizations { get; init; } = [];
}
