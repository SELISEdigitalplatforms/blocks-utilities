using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>Authoring and reading standalone credit offers. See spec 005.</summary>
public interface ICreditOfferService
{
    Task<SubscriptionOperationResult<IReadOnlyList<CreditOfferResponse>>> ListAsync(
        string? organizationId,
        string? meterKey,
        bool? enabled,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<CreditOfferResponse>> GetAsync(
        string meterKey,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<CreditOfferResponse>> CreateAsync(
        CreditOfferRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<CreditOfferResponse>> UpdateAsync(
        string meterKey,
        CreditOfferRequest request,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);
}
