using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

/// <summary>
/// Collapses a registration result to the single verdict a one-organization request produces.
/// </summary>
/// <remarks>
/// Registration reports one outcome per organization, but most of its behaviour — credentials,
/// encryption, the key ring, the uniqueness rule — has nothing to do with how many were named.
/// Those tests name one and assert on the verdict for it, exactly as the endpoint does.
/// </remarks>
internal static class SingleOrganizationRegistration
{
    public static async Task<PaymentOperationResult> RegisterOneAsync(
        this IPaymentProviderRegistrationService service,
        RegisterPaymentProviderRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var result = await service.RegisterAsync(request, correlationId, cancellationToken);

        if (result.Failure != null)
        {
            return result.Failure;
        }

        if (result.Organizations.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one organization, got {result.Organizations.Count}.");
        }

        var only = result.Organizations[0];

        return only.IsSuccess
            ? PaymentOperationResult.Success(
                new PaymentResponse
                {
                    PaymentDetailId = only.PaymentProviderId!,
                    ProviderName = request.ProviderName.ToUpperInvariant(),
                    PaymentStatus = only.Status
                },
                correlationId)
            : PaymentOperationResult.Failure(
                only.FailureKind,
                only.ErrorCode ?? string.Empty,
                only.ErrorMessage ?? string.Empty,
                correlationId);
    }

    /// <summary>The outcome for one named organization, so a multi-organization test can assert per organization.</summary>
    public static PaymentProviderRegistrationOutcome For(
        this PaymentProviderRegistrationResult result,
        string? organizationId) =>
        result.Organizations.Single(outcome =>
            string.Equals(outcome.OrganizationId, organizationId, StringComparison.Ordinal));

    public static PaymentFailureKind KindFor(
        this PaymentProviderRegistrationResult result,
        string? organizationId) =>
        result.For(organizationId).FailureKind;
}
